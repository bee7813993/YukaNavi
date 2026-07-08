using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YukaNavi.Api;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// 予約一覧画面。3秒間隔のポーリングで一覧を更新し、再生中の曲をハイライトする。
    /// 行タップで予約の詳細 (RequestDetailScreen) へ。未再生の行は長押しでドラッグ並べ替え。
    /// 一般のカラオケ端末の慣習に合わせ、操作は全ユーザーが行える。
    /// </summary>
    public class QueueScreen : ScreenBase
    {
        const float PollIntervalSeconds = 3f;
        /// <summary>行間 (CreateScrollList の spacing)。ドラッグの移動量計算に使う</summary>
        const float RowSpacing = 14f;
        const float DefaultRowHeight = 136f;

        Text _headerText;
        Text _statusText;
        RectTransform _listContent;
        ScrollRect _scrollRect;
        readonly List<GameObject> _rows = new List<GameObject>();
        string _lastSignature = "";
        bool _refreshing;
        bool _reordering;
        Coroutine _polling;
        GameObject _pauseButtonGo;
        Text _pauseLabel;
        float _pauseArmedAt = -100f;

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            // 上部バー
            UiFactory.CreateTopBar(transform, "予約一覧");

            // ヘッダー (残り件数・時間)
            _headerText = UiFactory.CreateText(transform, "Header", "", 30, UiFactory.PrimaryDark,
                TextAnchor.MiddleLeft);
            var headerRect = _headerText.rectTransform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = new Vector2(0f, -122f);
            headerRect.offsetMin = new Vector2(28f, headerRect.offsetMin.y);
            headerRect.offsetMax = new Vector2(-450f, headerRect.offsetMax.y);
            headerRect.sizeDelta = new Vector2(headerRect.sizeDelta.x, 46f);

            // 再生中の位置へ飛ぶボタン
            var jumpButton = UiFactory.CreateSoftButton(transform, "JumpPlaying", "▶ 再生中へ", 24);
            var jumpRect = jumpButton.GetComponent<RectTransform>();
            jumpRect.anchorMin = jumpRect.anchorMax = new Vector2(1f, 1f);
            jumpRect.pivot = new Vector2(1f, 1f);
            jumpRect.anchoredPosition = new Vector2(-20f, -118f);
            jumpRect.sizeDelta = new Vector2(230f, 56f);
            jumpButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                ScrollToPlaying();
            });

            // 小休止の挿入 (サーバーで「ユーザーによる小休止」が有効なときだけ表示)
            var pauseButton = UiFactory.CreateSoftButton(transform, "InsertPause", "＋小休止", 24);
            _pauseButtonGo = pauseButton.gameObject;
            _pauseLabel = pauseButton.GetComponentInChildren<Text>();
            var pauseRect = pauseButton.GetComponent<RectTransform>();
            pauseRect.anchorMin = pauseRect.anchorMax = new Vector2(1f, 1f);
            pauseRect.pivot = new Vector2(1f, 1f);
            pauseRect.anchoredPosition = new Vector2(-262f, -118f);
            pauseRect.sizeDelta = new Vector2(180f, 56f);
            pauseButton.onClick.AddListener(OnInsertPausePressed);
            _pauseButtonGo.SetActive(false);

            // ステータス行 (エラー・操作ヒント)
            _statusText = UiFactory.CreateText(transform, "Status", "", 24, UiFactory.TextMuted);
            var statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -172f);
            statusRect.sizeDelta = new Vector2(-40f, 36f);

            // 一覧
            var scrollRectT = UiFactory.CreateScrollList(transform, "QueueList", out _listContent);
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 16f);
            scrollRectT.offsetMax = new Vector2(-20f, -215f);
            _scrollRect = scrollRectT.GetComponent<ScrollRect>();
            if (_scrollRect == null)
            {
                _scrollRect = scrollRectT.GetComponentInChildren<ScrollRect>();
            }
        }

        public override void OnShow()
        {
            _lastSignature = "";
            _reordering = false;
            _pauseArmedAt = -100f;
            _pauseLabel.text = "＋小休止";
            SetStatus("", false);
            _ = ApplyCapabilitiesAsync();
            _polling = StartCoroutine(PollRoutine());
        }

        /// <summary>「ユーザーによる小休止」が有効なサーバーでだけ挿入ボタンを出す。</summary>
        async System.Threading.Tasks.Task ApplyCapabilitiesAsync()
        {
            try
            {
                var caps = await AppState.EnsureCapabilitiesAsync();
                _pauseButtonGo.SetActive(caps.Features != null && caps.Features.Userpause);
            }
            catch (System.Exception)
            {
                // 取得失敗時は非表示のまま
            }
        }

        /// <summary>小休止の挿入 (2度押し確認)。キューの最後に kind=小休止 の予約が入る。</summary>
        void OnInsertPausePressed()
        {
            if (Time.time - _pauseArmedAt > 3f)
            {
                _pauseArmedAt = Time.time;
                _pauseLabel.text = "もう一度で挿入";
                Se.Play(Se.Tap);
                return;
            }
            _pauseArmedAt = -100f;
            _pauseLabel.text = "＋小休止";
            _ = InsertPauseAsync();
        }

        async System.Threading.Tasks.Task InsertPauseAsync()
        {
            try
            {
                await AppConfig.CreateClient().PostRequestAsync(
                    "小休止", "", AppConfig.Username, "", "小休止");
                Se.Play(Se.Confirm);
                SetStatus("小休止を入れました", false);
                _ = RefreshAsync();
            }
            catch (System.Exception e)
            {
                SetStatus("小休止の挿入に失敗: " + e.Message, true);
                Se.Play(Se.Error);
            }
        }

        public override void OnHide()
        {
            if (_polling != null)
            {
                StopCoroutine(_polling);
                _polling = null;
            }
        }

        IEnumerator PollRoutine()
        {
            while (true)
            {
                _ = RefreshAsync();
                yield return new WaitForSeconds(PollIntervalSeconds);
            }
        }

        async Task RefreshAsync()
        {
            if (_refreshing || _reordering)
            {
                return; // 並べ替え中は行を作り直さない
            }
            _refreshing = true;
            try
            {
                var data = await AppConfig.CreateClient().GetRequestsAsync();
                if (_reordering)
                {
                    return; // 取得中に並べ替えが始まっていたら捨てる
                }
                int minutes = Mathf.CeilToInt(data.RemainingSeconds / 60f);
                _headerText.text = data.Total == 0
                    ? "予約はありません"
                    : $"全 {data.Total} 件 / 未再生 {data.RemainingCount} 件"
                      + (data.RemainingSeconds > 0 ? $" (約 {minutes} 分)" : "");
                SetStatus(data.Total > 0 ? "タップで詳細 / 長押しで並べ替え" : "", false);
                RebuildIfChanged(data.Items);
            }
            catch (System.Exception e)
            {
                SetStatus("更新に失敗: " + e.Message, true);
            }
            finally
            {
                _refreshing = false;
            }
        }

        /// <summary>内容が変わったときだけ行を作り直す (スクロール位置の維持のため)。</summary>
        void RebuildIfChanged(List<RequestItemDto> items)
        {
            var sig = new StringBuilder();
            if (items != null)
            {
                foreach (var item in items)
                {
                    sig.Append(item.Id).Append(':').Append(item.Nowplaying)
                       .Append(':').Append(item.Reqorder).Append('|');
                }
            }
            string signature = sig.ToString();
            if (signature == _lastSignature)
            {
                return;
            }
            _lastSignature = signature;

            foreach (var row in _rows)
            {
                Destroy(row);
            }
            _rows.Clear();
            if (items == null)
            {
                return;
            }
            foreach (var item in items)
            {
                AddRow(item);
            }
        }

        void AddRow(RequestItemDto item)
        {
            bool isPlaying = item.Nowplaying == "再生中";
            var rowGo = new GameObject("Row");
            rowGo.transform.SetParent(_listContent, false);
            bool isPending = item.Nowplaying == "未再生" || item.Nowplaying == "1";
            bool isDone = !isPlaying && !isPending;

            // シークレット予約 (未再生) は伏せ字のまま。曲情報も出さない
            bool masked = item.Secret == 1 && isPending;
            // ListerDB のきれいな曲名があればタイトルに使い、ファイル名は一覧では出さない
            // (ファイル名は詳細画面で確認できる)
            string title = (!masked && !string.IsNullOrEmpty(item.SongName))
                ? item.SongName : item.DisplayName;
            string artistLine = masked ? "" : (item.ListerArtist ?? "").Trim();
            string workLine = "";
            if (!masked && !string.IsNullOrEmpty(item.ListerWork))
            {
                workLine = item.ListerWork;
                if (!string.IsNullOrEmpty(item.ListerOpEd))
                {
                    workLine += " [" + item.ListerOpEd + "]";
                }
            }

            // 各行とも折り返して全文表示し、行の高さは内容に合わせて伸ばす (縦は広めに使う)
            // (テキスト幅 ≈ リスト幅 1040 - バッジ 118 - 右余白 24)
            int nameLines = UiFactory.EstimateWrapLines(title, 30, 870f);
            float nameHeight = nameLines * UiFactory.LineHeight(30);
            float artistHeight = artistLine != ""
                ? UiFactory.EstimateWrapLines(artistLine, 24, 870f) * UiFactory.LineHeight(24) + 4f : 0f;
            float workHeight = workLine != ""
                ? UiFactory.EstimateWrapLines(workLine, 24, 870f) * UiFactory.LineHeight(24) + 4f : 0f;
            // コメント (みんなで追記できる) は全文見せる
            string comment = masked ? "" : (item.Comment ?? "").Trim();
            float commentHeight = comment != ""
                ? UiFactory.EstimateWrapLines(comment, 22, 870f) * UiFactory.LineHeight(22) + 10f : 0f;
            float rowHeight = Mathf.Max(
                20f + nameHeight + 6f + artistHeight + workHeight + commentHeight + 12f + 62f + 16f,
                DefaultRowHeight);

            var img = rowGo.AddComponent<Image>();
            img.color = isPlaying ? UiFactory.PrimaryPale
                : (isDone ? new Color(0.965f, 0.955f, 0.985f) : UiFactory.CardBg);
            UiFactory.Roundify(img);
            UiFactory.AddShadow(rowGo, 3f);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = rowHeight;
            var button = rowGo.AddComponent<Button>();
            rowGo.AddComponent<PressEffect>();
            button.onClick.AddListener(() =>
            {
                Se.Play(Se.Transition);
                // カードが画面いっぱいに広がる演出の裏で詳細へ切り替える
                SpawnExpandGhost((RectTransform)rowGo.transform);
                RequestDetailScreen.Open(Manager, item);
            });

            // 未再生行は長押しでドラッグ並べ替え
            var drag = rowGo.AddComponent<RowDrag>();
            drag.Owner = this;
            drag.Item = item;
            drag.RowButton = button;

            // 左: 順番の丸バッジ (再生中 ▶ / 再生済 ✓ / 未再生は再生順)
            var circleGo = new GameObject("Order");
            circleGo.transform.SetParent(rowGo.transform, false);
            var circleImg = circleGo.AddComponent<Image>();
            circleImg.sprite = UiFactory.RoundedSprite;
            circleImg.type = Image.Type.Sliced;
            circleImg.pixelsPerUnitMultiplier = 0.55f; // ほぼ円形に見せる
            circleImg.color = isPlaying ? UiFactory.Primary
                : (isDone ? new Color(0.85f, 0.83f, 0.90f) : UiFactory.PrimaryPale);
            circleImg.raycastTarget = false;
            var circleRect = circleGo.GetComponent<RectTransform>();
            circleRect.anchorMin = circleRect.anchorMax = new Vector2(0f, 0.5f);
            circleRect.pivot = new Vector2(0f, 0.5f);
            circleRect.anchoredPosition = new Vector2(20f, 0f);
            circleRect.sizeDelta = new Vector2(78f, 78f);
            string mark = isPlaying ? "▶" : (isDone ? "✓" : item.Position.ToString());
            var circleText = UiFactory.CreateText(circleGo.transform, "Mark", mark, 32,
                isPlaying ? Color.white : UiFactory.PrimaryDark);
            UiFactory.StretchFull(circleText.rectTransform);

            // 中央: 曲名 (折り返して全文。単語折り返しはしない)
            var nameText = UiFactory.CreateText(rowGo.transform, "Name",
                UiFactory.NoWordWrap(title), 30,
                isDone ? UiFactory.TextMuted : UiFactory.TextDark, TextAnchor.UpperLeft);
            var nameRect = nameText.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.anchoredPosition = new Vector2(0f, -18f);
            nameRect.offsetMin = new Vector2(118f, nameRect.offsetMin.y);
            nameRect.offsetMax = new Vector2(-24f, nameRect.offsetMax.y);
            nameRect.sizeDelta = new Vector2(nameRect.sizeDelta.x, nameHeight);
            nameText.verticalOverflow = VerticalWrapMode.Overflow;

            float contentY = 18f + nameHeight + 6f;
            var infoColor = isDone ? UiFactory.TextMuted : new Color(0.45f, 0.42f, 0.55f);

            // 歌手 (独立行・全文)
            if (artistLine != "")
            {
                AddRowText(rowGo.transform, "Artist", artistLine, 24, infoColor,
                    contentY, artistHeight);
                contentY += artistHeight;
            }

            // 作品 [OP/ED] (独立行・全文)
            if (workLine != "")
            {
                AddRowText(rowGo.transform, "Work", workLine, 24, infoColor,
                    contentY, workHeight);
                contentY += workHeight;
            }

            // コメント (追記形式の全文。詳細画面から誰でも追記できる)
            if (comment != "")
            {
                contentY += 6f;
                AddRowText(rowGo.transform, "Comment", comment, 22,
                    isDone ? UiFactory.TextMuted : UiFactory.TextDark,
                    contentY, commentHeight);
            }

            // 下段左: うたう人のピルバッジ (Web 版の登録者バッジ相当)
            if (!string.IsNullOrEmpty(item.Singer))
            {
                string singerLabel = "うたう人: " + item.Singer;
                var singerBadge = UiFactory.CreateBadge(rowGo.transform, "Singer", singerLabel,
                    isDone ? new Color(0.92f, 0.91f, 0.95f) : UiFactory.PrimaryPale,
                    isDone ? UiFactory.TextMuted : UiFactory.PrimaryDark);
                var singerRect = (RectTransform)singerBadge.transform.parent;
                singerRect.anchorMin = singerRect.anchorMax = new Vector2(0f, 0f);
                singerRect.pivot = new Vector2(0f, 0f);
                singerRect.anchoredPosition = new Vector2(118f, 12f);
                singerRect.sizeDelta = new Vector2(
                    Mathf.Min(UiFactory.EstimateTextWidth(singerLabel, 22) + 36f,
                        isPlaying ? 340f : 620f), 46f);
            }

            // 下段右: 状態バッジ (未再生 / ♪ 再生中 / 再生済)
            string stateLabel = isPlaying ? "♪ 再生中" : (isDone ? "再生済" : "未再生");
            float stateBadgeWidth = UiFactory.EstimateTextWidth(stateLabel, 22) + 40f;
            var stateBadge = UiFactory.CreateBadge(rowGo.transform, "State", stateLabel,
                isPlaying ? UiFactory.Primary
                    : (isDone ? new Color(0.88f, 0.87f, 0.92f) : UiFactory.PrimaryDark),
                isPlaying ? Color.white
                    : (isDone ? UiFactory.TextMuted : Color.white));
            var stateRect = (RectTransform)stateBadge.transform.parent;
            stateRect.anchorMin = stateRect.anchorMax = new Vector2(1f, 0f);
            stateRect.pivot = new Vector2(1f, 0f);
            stateRect.anchoredPosition = new Vector2(-16f, 12f);
            stateRect.sizeDelta = new Vector2(stateBadgeWidth, 46f);

            // 再生中の曲は一覧から直接終了できる (BGV・小休止は終了させないと次に進まない)
            if (isPlaying)
            {
                var endButton = UiFactory.CreateButton(rowGo.transform, "EndSong", "曲を終了",
                    UiFactory.Danger, Color.white, 22);
                var endRect = endButton.GetComponent<RectTransform>();
                endRect.anchorMin = endRect.anchorMax = new Vector2(1f, 0f);
                endRect.pivot = new Vector2(1f, 0f);
                endRect.anchoredPosition = new Vector2(-(stateBadgeWidth + 28f), 12f);
                endRect.sizeDelta = new Vector2(190f, 46f);
                var endLabel = endButton.GetComponentInChildren<Text>();
                bool armed = false; // 誤操作防止の2度押し
                endButton.onClick.AddListener(async () =>
                {
                    if (!armed)
                    {
                        armed = true;
                        endLabel.text = "もう一度で終了";
                        endRect.sizeDelta = new Vector2(240f, 46f);
                        Se.Play(Se.Tap);
                        return;
                    }
                    try
                    {
                        await AppConfig.CreateClient().PlayerActionAsync("next");
                        Se.Play(Se.Confirm);
                        _lastSignature = "";
                        _ = RefreshAsync();
                    }
                    catch (System.Exception e)
                    {
                        SetStatus("曲の終了に失敗: " + e.Message, true);
                        Se.Play(Se.Error);
                    }
                });
            }

            _rows.Add(rowGo);
        }

        void SetStatus(string message, bool isError)
        {
            _statusText.text = message;
            _statusText.color = isError ? UiFactory.Danger : UiFactory.TextMuted;
        }

        /// <summary>再生中の行 (無ければ次に再生される未再生の行 = 一番下の未再生) へスクロールする。</summary>
        void ScrollToPlaying()
        {
            Transform target = null;
            foreach (Transform child in _listContent)
            {
                var rowDrag = child.GetComponent<RowDrag>();
                if (rowDrag == null)
                {
                    continue;
                }
                if (rowDrag.Item.Nowplaying == "再生中")
                {
                    target = child;
                    break;
                }
                if (CanReorder(rowDrag.Item))
                {
                    target = child; // 上から走査して最後に残った未再生 = 次に再生される曲
                }
            }
            if (target == null || _scrollRect == null)
            {
                return;
            }
            float contentHeight = _listContent.rect.height;
            float viewportHeight = _scrollRect.viewport != null
                ? _scrollRect.viewport.rect.height : ((RectTransform)_scrollRect.transform).rect.height;
            float scrollable = Mathf.Max(contentHeight - viewportHeight, 1f);
            // ターゲットが画面の上 1/4 あたりに来る位置へ
            float targetTop = -((RectTransform)target).anchoredPosition.y;
            float normalized = 1f - Mathf.Clamp01((targetTop - viewportHeight * 0.25f) / scrollable);
            _scrollRect.verticalNormalizedPosition = normalized;
        }

        /// <summary>
        /// タップした行の位置から白いカードが画面いっぱいに広がるゴースト演出。
        /// 画面遷移後も動き続けるよう、ゴースト自身がアニメーションを持つ。
        /// </summary>
        void SpawnExpandGhost(RectTransform rowRect)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return;
            }
            var canvasRect = (RectTransform)canvas.transform;
            var ghostGo = new GameObject("ExpandGhost");
            ghostGo.transform.SetParent(canvasRect, false);
            var img = ghostGo.AddComponent<Image>();
            img.color = UiFactory.CardBg;
            UiFactory.Roundify(img);
            var ghost = (RectTransform)ghostGo.transform;
            ghost.anchorMin = ghost.anchorMax = new Vector2(0.5f, 0.5f);
            ghost.pivot = new Vector2(0.5f, 0.5f);
            var corners = new Vector3[4];
            rowRect.GetWorldCorners(corners);
            Vector2 min = canvasRect.InverseTransformPoint(corners[0]);
            Vector2 max = canvasRect.InverseTransformPoint(corners[2]);
            ghost.SetAsLastSibling();

            var anim = ghostGo.AddComponent<ExpandGhost>();
            anim.StartPos = (min + max) * 0.5f;
            anim.StartSize = max - min;
            anim.EndSize = canvasRect.rect.size;
        }

        /// <summary>広がるゴーストのアニメーション本体 (終わると自分を消す)。</summary>
        class ExpandGhost : MonoBehaviour
        {
            public Vector2 StartPos;
            public Vector2 StartSize;
            public Vector2 EndSize;

            IEnumerator Start()
            {
                var rect = (RectTransform)transform;
                var canvasGroup = gameObject.AddComponent<CanvasGroup>();
                canvasGroup.blocksRaycasts = false;
                const float duration = 0.22f;
                for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
                {
                    float k = elapsed / duration;
                    float ease = 1f - (1f - k) * (1f - k);
                    rect.anchoredPosition = Vector2.Lerp(StartPos, Vector2.zero, ease);
                    rect.sizeDelta = Vector2.Lerp(StartSize, EndSize, ease);
                    canvasGroup.alpha = 1f - ease * ease; // 後半で薄れて詳細画面が見えてくる
                    yield return null;
                }
                Destroy(gameObject);
            }
        }

        /// <summary>入れ替わった相手の行を旧位置からスッと滑らせる (レイアウト確定後に実行)。</summary>
        IEnumerator AnimateSwapRoutine(RectTransform rect, float fromOffset)
        {
            yield return null; // VerticalLayoutGroup の再配置を待つ
            if (rect == null)
            {
                yield break;
            }
            Vector2 basePos = rect.anchoredPosition;
            const float duration = 0.14f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                if (rect == null)
                {
                    yield break;
                }
                float k = 1f - elapsed / duration;
                rect.anchoredPosition = basePos + new Vector2(0f, fromOffset * k * k);
                yield return null;
            }
            if (rect != null)
            {
                rect.anchoredPosition = basePos;
            }
        }

        /// <summary>行内の左 118px から始まる折り返しテキスト (全文表示)。</summary>
        static void AddRowText(Transform row, string name, string label, int fontSize,
                               Color color, float y, float height)
        {
            var text = UiFactory.CreateText(row, name, UiFactory.NoWordWrap(label), fontSize,
                color, TextAnchor.UpperLeft);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.offsetMin = new Vector2(118f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-24f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            text.verticalOverflow = VerticalWrapMode.Overflow;
        }

        // ---- ドラッグ並べ替え ----

        static bool CanReorder(RequestItemDto item)
        {
            return item.Nowplaying == "未再生" || item.Nowplaying == "1";
        }

        void BeginLift(RowDrag drag)
        {
            _reordering = true;
            if (_scrollRect != null)
            {
                _scrollRect.enabled = false; // 並べ替え中はスクロールを止める
            }
            drag.RowButton.interactable = false; // 離したときの誤タップ (詳細遷移) を防ぐ
            var img = drag.GetComponent<Image>();
            img.color = UiFactory.PrimaryPale;
            drag.transform.localScale = new Vector3(1.02f, 1.02f, 1f);
            Se.Play(Se.Tap);
        }

        /// <summary>指定方向の隣の行1つ分の移動量 (隣の行高 + 行間)。動けないときは MaxValue。</summary>
        float StepFor(Transform row, int direction)
        {
            int target = row.GetSiblingIndex() + direction;
            if (target < 0 || target >= _listContent.childCount)
            {
                return float.MaxValue;
            }
            var other = _listContent.GetChild(target).GetComponent<RowDrag>();
            if (other == null || !CanReorder(other.Item))
            {
                return float.MaxValue; // 再生中・再生済みの行はまたげない
            }
            var le = other.GetComponent<LayoutElement>();
            return (le != null ? le.preferredHeight : DefaultRowHeight) + RowSpacing;
        }

        /// <summary>持ち上げた行を1つ上/下の未再生行と入れ替える (相手は滑って収まる)。</summary>
        void MoveRow(Transform row, int direction)
        {
            int index = row.GetSiblingIndex();
            int target = index + direction;
            if (target < 0 || target >= _listContent.childCount)
            {
                return;
            }
            var other = _listContent.GetChild(target).GetComponent<RowDrag>();
            if (other == null || !CanReorder(other.Item))
            {
                return;
            }
            var liftLe = row.GetComponent<LayoutElement>();
            float step = (liftLe != null ? liftLe.preferredHeight : DefaultRowHeight) + RowSpacing;
            row.SetSiblingIndex(target);
            // 相手の行は旧位置 (持ち上げ行の分だけ direction 側) から新位置へ滑る
            StartCoroutine(AnimateSwapRoutine((RectTransform)other.transform, -direction * step));
        }

        async void EndLift(RowDrag drag)
        {
            if (_scrollRect != null)
            {
                _scrollRect.enabled = true;
            }
            var img = drag.GetComponent<Image>();
            img.color = UiFactory.CardBg;
            drag.transform.localScale = Vector3.one;

            // 表示順 (上から) の id をサーバーへ送る
            var ids = new List<int>();
            foreach (Transform child in _listContent)
            {
                var rowDrag = child.GetComponent<RowDrag>();
                if (rowDrag != null)
                {
                    ids.Add(rowDrag.Item.Id);
                }
            }
            try
            {
                await AppConfig.CreateClient().ReorderRequestsAsync(ids);
                Se.Play(Se.Confirm);
            }
            catch (System.Exception e)
            {
                SetStatus("並べ替えに失敗: " + e.Message, true);
                Se.Play(Se.Error);
            }
            _reordering = false;
            _lastSignature = ""; // 次の取得で位置バッジ等を作り直す
            _ = RefreshAsync();
        }

        /// <summary>
        /// 行の長押しドラッグ。通常のドラッグは ScrollRect へ転送してスクロールとして扱い、
        /// 長押し (0.45秒) が成立したときだけ並べ替えモードに入る。
        /// </summary>
        class RowDrag : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
                        IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            const float LongPressSeconds = 0.45f;

            public QueueScreen Owner;
            public RequestItemDto Item;
            public Button RowButton;

            bool _pressed;
            float _downTime;
            bool _lifting;
            bool _forwarding;
            float _accumY;

            public void OnPointerDown(PointerEventData eventData)
            {
                _pressed = true;
                _downTime = Time.unscaledTime;
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                _pressed = false;
                if (_lifting && !eventData.dragging)
                {
                    Finish(); // 長押し後、ドラッグせずに離した
                }
            }

            void Update()
            {
                if (_pressed && !_lifting && Time.unscaledTime - _downTime >= LongPressSeconds)
                {
                    _pressed = false;
                    if (CanReorder(Item) && !Owner._reordering)
                    {
                        _lifting = true;
                        _accumY = 0f;
                        Owner.BeginLift(this);
                    }
                }
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                // ドラッグが始まったらタップ (詳細遷移) は無効にする。
                // pointerDrag がこの行自身になるため、UGUI 標準のクリック取り消しが
                // 働かない (押した対象とドラッグ対象が同じだと eligibleForClick が残る)
                eventData.eligibleForClick = false;
                if (_lifting)
                {
                    return;
                }
                // 長押し前のドラッグ = スクロール。ScrollRect へ転送する
                _pressed = false;
                _forwarding = true;
                ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData,
                    ExecuteEvents.beginDragHandler);
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (_forwarding)
                {
                    ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData,
                        ExecuteEvents.dragHandler);
                    return;
                }
                if (!_lifting)
                {
                    return;
                }
                var canvas = GetComponentInParent<Canvas>();
                float scale = (canvas != null && canvas.scaleFactor > 0f) ? canvas.scaleFactor : 1f;
                _accumY += eventData.delta.y / scale;
                // 隣の行1つ分 (行高は可変) 動くごとに入れ替える
                while (true)
                {
                    float stepDown = Owner.StepFor(transform, +1);
                    if (_accumY <= -stepDown)
                    {
                        _accumY += stepDown;
                        Owner.MoveRow(transform, +1);
                        continue;
                    }
                    float stepUp = Owner.StepFor(transform, -1);
                    if (_accumY >= stepUp)
                    {
                        _accumY -= stepUp;
                        Owner.MoveRow(transform, -1);
                        continue;
                    }
                    break;
                }
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                if (_forwarding)
                {
                    ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData,
                        ExecuteEvents.endDragHandler);
                    _forwarding = false;
                    return;
                }
                if (_lifting)
                {
                    Finish();
                }
            }

            void Finish()
            {
                _lifting = false;
                Owner.EndLift(this);
            }
        }
    }
}
