using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// マイページ画面。あとで歌う / お気に入り曲 / お気に入り検索 / うたった曲 の一覧を表示し、
    /// 行タップからもう一度予約 (検索はタップで実行) できる。
    /// うたった曲は使ううちに溜まるため一番右のタブに置き、回数の多い順に表示、
    /// 「えらんで消す」(複数選択) と「ぜんぶ消す」で整理できる。
    /// 未リンク時は端末ローカル (LocalMypage)、サーバー連携 (デバイスリンク) 済みなら
    /// Web 版と同じサーバーデータを表示する (MypageService が振り分ける)。
    /// </summary>
    public class MypageScreen : ScreenBase
    {
        enum Tab
        {
            Later,
            Favorite,
            Search,
            History,
        }

        static int _pendingTab = -1;

        /// <summary>タブを指定して開く (0=あとで歌う 1=お気に入り曲 2=お気に入り検索 3=うたった曲)。検索トップの動線用。</summary>
        public static void Open(ScreenManager manager, int tab)
        {
            _pendingTab = tab;
            manager.Show<MypageScreen>();
        }

        Tab _tab = Tab.Later;
        Button[] _tabs;
        Text _statusText;
        RectTransform _scrollRect;
        RectTransform _listContent;
        readonly List<GameObject> _rows = new List<GameObject>();
        int _reloadSerial;
        Button _linkButton;
        Text _linkButtonLabel;

        // うたった曲の整理 (えらんで消す / ぜんぶ消す)
        GameObject _historyTools;
        Button _selectModeButton;
        Button _clearAllButton;
        Button _deleteSelectedButton;
        Text _deleteSelectedLabel;
        Button _cancelSelectButton;
        bool _selectMode;
        readonly HashSet<string> _selected = new HashSet<string>();
        int _historyCount;
        GameObject _confirmModal;

        public override void BuildUi()
        {
            // RebuildAll (テーマ変更等) で作り直されたときの状態リセット
            _selectMode = false;
            _selected.Clear();
            _confirmModal = null;

            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            var topBar = UiFactory.CreateTopBar(transform, "マイページ");

            // サーバー連携 (デバイスリンク) 画面への導線。リンク済みはテーマ色で示す
            _linkButton = UiFactory.CreateButton(topBar, "Link", "連携",
                UiFactory.PrimaryPale, UiFactory.Primary, 24);
            _linkButtonLabel = _linkButton.GetComponentInChildren<Text>();
            UiFactory.FitLabelOneLine(_linkButtonLabel);
            var linkRect = _linkButton.GetComponent<RectTransform>();
            linkRect.anchorMin = linkRect.anchorMax = new Vector2(1f, 0.5f);
            linkRect.pivot = new Vector2(1f, 0.5f);
            linkRect.anchoredPosition = new Vector2(-20f, 0f);
            linkRect.sizeDelta = new Vector2(150f, 72f);
            _linkButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Transition);
                Manager.Show<MypageLinkScreen>();
            });
            UpdateLinkButton();

            // タブ (あとで歌う / お気に入り曲 / お気に入り検索 / うたった曲)
            var tabBar = UiFactory.CreatePanel(transform, "Tabs");
            tabBar.anchorMin = new Vector2(0f, 1f);
            tabBar.anchorMax = new Vector2(1f, 1f);
            tabBar.pivot = new Vector2(0.5f, 1f);
            tabBar.anchoredPosition = new Vector2(0f, -118f);
            tabBar.offsetMin = new Vector2(20f, tabBar.offsetMin.y);
            tabBar.offsetMax = new Vector2(-20f, tabBar.offsetMax.y);
            tabBar.sizeDelta = new Vector2(tabBar.sizeDelta.x, 80f);
            _tabs = UiFactory.CreateSegmentTabs(tabBar,
                new[] { "あとで歌う", "お気に入り曲", "お気に入り検索", "うたった曲" }, 23);
            for (int i = 0; i < _tabs.Length; i++)
            {
                var tab = (Tab)i;
                _tabs[i].onClick.AddListener(() => SetTab(tab));
            }

            // 上寄せ + FontScale 込みの高さで、大きい文字設定でも下の整理ボタン行に食い込まない
            _statusText = UiFactory.CreateText(transform, "Status", "", 28, UiFactory.TextDark,
                TextAnchor.UpperCenter);
            var statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -200f);
            statusRect.sizeDelta = new Vector2(-40f, UiFactory.LineHeight(28));

            BuildHistoryTools();

            _scrollRect = UiFactory.CreateScrollList(transform, "MypageList", out _listContent);
            _scrollRect.anchorMin = new Vector2(0f, 0f);
            _scrollRect.anchorMax = new Vector2(1f, 1f);
            _scrollRect.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 16f);
            _scrollRect.offsetMax = new Vector2(-20f, -248f);

            UpdateTabColors();
        }

        /// <summary>うたった曲タブの整理ボタン行 (えらんで消す / ぜんぶ消す)。</summary>
        void BuildHistoryTools()
        {
            var tools = UiFactory.CreatePanel(transform, "HistoryTools");
            tools.anchorMin = new Vector2(0f, 1f);
            tools.anchorMax = new Vector2(1f, 1f);
            tools.pivot = new Vector2(0.5f, 1f);
            tools.anchoredPosition = new Vector2(0f, -258f);
            tools.sizeDelta = new Vector2(0f, 64f);
            _historyTools = tools.gameObject;

            _selectModeButton = UiFactory.CreateSoftButton(tools, "SelectMode", "えらんで消す", 24);
            UiFactory.FitLabelOneLine(_selectModeButton.GetComponentInChildren<Text>());
            SetToolButton(_selectModeButton, -20f, 250f);
            _selectModeButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                _selectMode = true;
                _selected.Clear();
                UpdateHistoryTools();
                Reload();
            });

            _clearAllButton = UiFactory.CreateSoftButton(tools, "ClearAll", "ぜんぶ消す", 24);
            UiFactory.FitLabelOneLine(_clearAllButton.GetComponentInChildren<Text>());
            SetToolButton(_clearAllButton, -282f, 220f);
            _clearAllButton.onClick.AddListener(ConfirmClearAll);

            _deleteSelectedButton = UiFactory.CreateButton(tools, "DeleteSelected", "削除 (0)",
                UiFactory.Danger, Color.white, 24);
            _deleteSelectedLabel = _deleteSelectedButton.GetComponentInChildren<Text>();
            UiFactory.FitLabelOneLine(_deleteSelectedLabel);
            SetToolButton(_deleteSelectedButton, -20f, 250f);
            _deleteSelectedButton.onClick.AddListener(ConfirmDeleteSelected);

            _cancelSelectButton = UiFactory.CreateSoftButton(tools, "CancelSelect", "やめる", 24);
            UiFactory.FitLabelOneLine(_cancelSelectButton.GetComponentInChildren<Text>());
            SetToolButton(_cancelSelectButton, -282f, 170f);
            _cancelSelectButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                ExitSelectMode();
            });

            _historyTools.SetActive(false);
        }

        static void SetToolButton(Button button, float x, float width)
        {
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, 64f);
        }

        public override void OnShow()
        {
            _selectMode = false;
            _selected.Clear();
            if (_pendingTab >= 0 && _pendingTab <= 3)
            {
                _tab = (Tab)_pendingTab;
                _pendingTab = -1;
                UpdateTabColors();
            }
            UpdateLinkButton(); // 連携画面から戻ったときに状態を反映
            Reload();
        }

        public override void OnHide()
        {
            // 画面を離れたら破壊的操作の確認は無効にする (開いたまま他画面へ移り、
            // 後で戻ったときに古い選択に対する削除が実行できてしまうのを防ぐ)
            CloseConfirm();
        }

        public override bool OnBackRequested()
        {
            if (_confirmModal != null)
            {
                CloseConfirm();
                return true;
            }
            if (_selectMode)
            {
                ExitSelectMode();
                return true;
            }
            return false;
        }

        void ExitSelectMode()
        {
            _selectMode = false;
            _selected.Clear();
            UpdateHistoryTools();
            Reload();
        }

        void UpdateLinkButton()
        {
            bool linked = MypageService.IsLinked;
            _linkButtonLabel.text = linked ? "連携中" : "連携";
            _linkButton.image.color = linked ? UiFactory.Primary : UiFactory.PrimaryPale;
            _linkButtonLabel.color = linked ? Color.white : UiFactory.Primary;
        }

        void SetTab(Tab tab)
        {
            Se.Play(Se.Tap);
            if (_tab == tab)
            {
                return;
            }
            _tab = tab;
            _selectMode = false;
            _selected.Clear();
            UpdateTabColors();
            Reload();
        }

        void UpdateTabColors()
        {
            UiFactory.SetSegmentSelected(_tabs, (int)_tab);
        }

        /// <summary>
        /// 整理ボタン行の表示を現在の状態 (タブ・選択モード・件数) に合わせる。
        /// 選択モード中は件数表示も「N 件を選択中」に置き換える。
        /// </summary>
        void UpdateHistoryTools()
        {
            bool show = _tab == Tab.History && _historyCount > 0;
            _historyTools.SetActive(show);
            _selectModeButton.gameObject.SetActive(!_selectMode);
            _clearAllButton.gameObject.SetActive(!_selectMode);
            _deleteSelectedButton.gameObject.SetActive(_selectMode);
            _cancelSelectButton.gameObject.SetActive(_selectMode);
            if (show && _selectMode)
            {
                _deleteSelectedLabel.text = "削除 (" + _selected.Count + ")";
                _deleteSelectedButton.interactable = _selected.Count > 0;
                _statusText.text = _selected.Count + " 件を選択中";
            }
        }

        async void Reload()
        {
            int serial = ++_reloadSerial;
            foreach (var row in _rows)
            {
                Destroy(row);
            }
            _rows.Clear();
            _statusText.text = "";
            _historyCount = 0;
            UpdateHistoryTools();
            // うたった曲タブは整理ボタン行のぶん、リストの上端を下げる
            _scrollRect.offsetMax = new Vector2(-20f, _tab == Tab.History ? -330f : -248f);
            if (MypageService.IsLinked)
            {
                ShowLoading();
            }

            var tab = _tab;
            try
            {
                // お気に入り検索 (保存した検索条件) は曲とは別の行を出す
                if (tab == Tab.Search)
                {
                    var searches = await MypageService.GetSavedSearchesAsync();
                    if (serial != _reloadSerial)
                    {
                        return; // タブが切り替わった
                    }
                    HideLoading();
                    if (searches.Count == 0)
                    {
                        _statusText.text = "まだありません (検索結果の ☆ で保存できます)";
                        return;
                    }
                    _statusText.text = searches.Count + " 件";
                    foreach (var search in searches)
                    {
                        AddSearchRow(search);
                    }
                    return;
                }

                List<LocalMypage.Item> items;
                switch (tab)
                {
                    case Tab.Later:
                        items = await MypageService.GetLaterAsync();
                        break;
                    case Tab.Favorite:
                        items = await MypageService.GetFavoriteAsync();
                        break;
                    default:
                        items = await MypageService.GetHistoryAsync();
                        break;
                }
                if (serial != _reloadSerial)
                {
                    return;
                }
                HideLoading();

                if (items.Count == 0)
                {
                    _statusText.text = tab == Tab.History
                        ? "まだありません (予約すると記録されます)"
                        : "まだありません (予約画面から追加できます)";
                    return;
                }
                _statusText.text = items.Count + " 件";
                foreach (var item in items)
                {
                    AddRow(item);
                }
                if (tab == Tab.History)
                {
                    _historyCount = items.Count;
                    UpdateHistoryTools();
                }
            }
            catch (System.Exception e)
            {
                if (serial == _reloadSerial)
                {
                    HideLoading();
                    _statusText.text = "取得に失敗: " + e.Message;
                }
            }
        }

        // ---- うたった曲の一括削除 ----

        void ConfirmDeleteSelected()
        {
            if (_selected.Count == 0)
            {
                return;
            }
            Se.Play(Se.Tap);
            var paths = new List<string>(_selected);
            ShowConfirm("えらんだ曲を削除する？",
                "うたった曲から " + paths.Count + " 曲を削除します。\nこの操作は取り消せません。",
                async () =>
                {
                    ShowLoading("削除中...");
                    try
                    {
                        await MypageService.RemoveHistoryManyAsync(paths,
                            (done, total) => _statusText.text = "削除中... " + done + "/" + total);
                        Se.Play(Se.Confirm);
                        UiFactory.ShowToast(paths.Count + " 曲を削除しました");
                    }
                    catch (System.Exception e)
                    {
                        UiFactory.ShowToast("削除に失敗: " + e.Message, true);
                        Se.Play(Se.Error);
                    }
                    _selectMode = false;
                    _selected.Clear();
                    Reload();
                });
        }

        void ConfirmClearAll()
        {
            Se.Play(Se.Tap);
            string body = "うたった曲の記録をすべて削除します。\nこの操作は取り消せません。";
            if (MypageService.IsLinked)
            {
                body += "\n(サーバーに保存された履歴も削除します)";
            }
            ShowConfirm("うたった曲をぜんぶ削除する？", body,
                async () =>
                {
                    ShowLoading("削除中...");
                    try
                    {
                        await MypageService.ClearHistoryAsync(
                            (done, total) => _statusText.text = "削除中... " + done + "/" + total);
                        Se.Play(Se.Confirm);
                        UiFactory.ShowToast("うたった曲をぜんぶ削除しました");
                    }
                    catch (System.Exception e)
                    {
                        UiFactory.ShowToast("削除に失敗: " + e.Message, true);
                        Se.Play(Se.Error);
                    }
                    _selectMode = false;
                    _selected.Clear();
                    Reload();
                });
        }

        /// <summary>破壊的操作の確認モーダル (OK は赤ボタン)。開くたびに作り、閉じるときに破棄する。</summary>
        void ShowConfirm(string title, string body, System.Action onOk)
        {
            CloseConfirm();
            _confirmModal = new GameObject("ConfirmModal");
            _confirmModal.transform.SetParent(transform, false);
            UiFactory.StretchFull(_confirmModal.AddComponent<RectTransform>());
            var overlay = _confirmModal.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.55f);
            var overlayButton = _confirmModal.AddComponent<Button>();
            overlayButton.transition = Selectable.Transition.None;
            overlayButton.onClick.AddListener(CloseConfirm);

            float y = 28f;
            // 大きい文字設定では折り返しが増えるため、タイトル・本文とも行数を実測して確保する
            float titleH = UiFactory.EstimateWrapLines(title, 34, 780f) * UiFactory.LineHeight(34);
            float bodyH = UiFactory.EstimateWrapLines(body, 26, 780f) * UiFactory.LineHeight(26);
            float cardH = y + titleH + 16f + bodyH + 28f + 92f + 28f;

            var card = UiFactory.CreatePanel(_confirmModal.transform, "Card", Color.white);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(880f, cardH);
            UiFactory.Roundify(card.GetComponent<Image>());
            UiFactory.AddShadow(card.gameObject, 6f);
            var cardButton = card.gameObject.AddComponent<Button>(); // カード内タップの抜け防止
            cardButton.transition = Selectable.Transition.None;

            var titleText = UiFactory.CreateText(card, "Title", title, 34, UiFactory.PrimaryDark);
            SetModalRow(titleText.rectTransform, -y, titleH);
            y += titleH + 16f;

            var bodyText = UiFactory.CreateText(card, "Body", body, 26, UiFactory.TextDark);
            SetModalRow(bodyText.rectTransform, -y, bodyH);
            y += bodyH + 28f;

            var okButton = UiFactory.CreateButton(card, "Ok", "削除する", UiFactory.Danger, Color.white, 30);
            var okRect = okButton.GetComponent<RectTransform>();
            okRect.anchorMin = okRect.anchorMax = new Vector2(0.5f, 1f);
            okRect.pivot = new Vector2(0.5f, 1f);
            okRect.anchoredPosition = new Vector2(-190f, -y);
            okRect.sizeDelta = new Vector2(340f, 92f);
            okButton.onClick.AddListener(() =>
            {
                CloseConfirm();
                onOk();
            });

            var cancelButton = UiFactory.CreateSoftButton(card, "Cancel", "やめる", 30);
            var cancelRect = cancelButton.GetComponent<RectTransform>();
            cancelRect.anchorMin = cancelRect.anchorMax = new Vector2(0.5f, 1f);
            cancelRect.pivot = new Vector2(0.5f, 1f);
            cancelRect.anchoredPosition = new Vector2(190f, -y);
            cancelRect.sizeDelta = new Vector2(340f, 92f);
            cancelButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                CloseConfirm();
            });
        }

        void CloseConfirm()
        {
            if (_confirmModal != null)
            {
                Destroy(_confirmModal);
                _confirmModal = null;
            }
        }

        static void SetModalRow(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.offsetMin = new Vector2(50f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-50f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        static string FormatDate(long unixtime)
        {
            return System.DateTimeOffset.FromUnixTimeSeconds(unixtime).ToLocalTime().ToString("M/d HH:mm");
        }

        static string SearchKindLabel(LocalMypage.SavedSearch item)
        {
            switch (item.Field)
            {
                case "artist":
                    return "歌手";
                case "program":
                    return "作品";
                case "group":
                    return "シリーズ";
                case "worker":
                    return "動画制作";
            }
            return item.Kind == "everything" ? "キーワード (ファイル名)" : "キーワード (アニソンDB)";
        }

        /// <summary>お気に入り検索の行 (タップで検索実行 / 削除は2度押し)。</summary>
        void AddSearchRow(LocalMypage.SavedSearch item)
        {
            // 行の高さと各段の位置は文字の大きさ設定に追従させる (固定だと大きい設定で行が消える)
            float kindH = UiFactory.LineHeight(22);
            float valueH = UiFactory.LineHeight(30);
            var rowGo = new GameObject("Row");
            rowGo.transform.SetParent(_listContent, false);
            var img = rowGo.AddComponent<Image>();
            img.color = UiFactory.CardBg;
            UiFactory.Roundify(img);
            UiFactory.AddShadow(rowGo, 3f);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = Mathf.Max(118f, 12f + kindH + 2f + valueH + 12f);
            var button = rowGo.AddComponent<Button>();
            rowGo.AddComponent<PressEffect>();
            button.onClick.AddListener(() =>
            {
                Se.Play(Se.Transition);
                SearchResultScreen.Open(Manager, SearchResultScreen.FromSavedSearch(item));
            });

            // 上段: 種類 / 下段: 検索の値
            var kind = UiFactory.CreateText(rowGo.transform, "Kind", SearchKindLabel(item), 22,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            var kindRect = kind.rectTransform;
            kindRect.anchorMin = new Vector2(0f, 1f);
            kindRect.anchorMax = new Vector2(1f, 1f);
            kindRect.pivot = new Vector2(0.5f, 1f);
            kindRect.anchoredPosition = new Vector2(0f, -10f);
            kindRect.offsetMin = new Vector2(28f, kindRect.offsetMin.y);
            kindRect.offsetMax = new Vector2(-160f, kindRect.offsetMax.y);
            kindRect.sizeDelta = new Vector2(kindRect.sizeDelta.x, kindH);

            string value = !string.IsNullOrEmpty(item.Value) ? item.Value : item.Keyword;
            var title = UiFactory.CreateText(rowGo.transform, "Title", UiFactory.NoWordWrap(value ?? ""),
                30, UiFactory.TextDark, TextAnchor.MiddleLeft);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -(10f + kindH + 2f));
            titleRect.offsetMin = new Vector2(28f, titleRect.offsetMin.y);
            titleRect.offsetMax = new Vector2(-160f, titleRect.offsetMax.y);
            titleRect.sizeDelta = new Vector2(titleRect.sizeDelta.x, valueH);
            UiFactory.FitLabel(title); // 長い検索ワードは枠内に縮めて収める

            // 削除 (2度押し確認、曲の行と同じ様式)
            var deleteButton = UiFactory.CreateButton(rowGo.transform, "Delete", "削除",
                UiFactory.Danger, Color.white, 24);
            var delRect = deleteButton.GetComponent<RectTransform>();
            delRect.anchorMin = delRect.anchorMax = new Vector2(1f, 0.5f);
            delRect.pivot = new Vector2(1f, 0.5f);
            delRect.anchoredPosition = new Vector2(-16f, 0f);
            delRect.sizeDelta = new Vector2(120f, 76f);
            var delLabel = deleteButton.GetComponentInChildren<Text>();
            bool armed = false;
            deleteButton.onClick.AddListener(async () =>
            {
                if (!armed)
                {
                    armed = true;
                    delLabel.text = "本当に？";
                    Se.Play(Se.Tap);
                    return;
                }
                try
                {
                    await MypageService.ToggleSavedSearchAsync(item); // 保存済みなので解除になる
                    Se.Play(Se.Confirm);
                }
                catch (System.Exception e)
                {
                    UiFactory.ShowToast("削除に失敗: " + e.Message, true);
                    Se.Play(Se.Error);
                }
                Reload();
            });

            _rows.Add(rowGo);
        }

        void AddRow(LocalMypage.Item item)
        {
            string line2 = _tab == Tab.History
                ? "最終: " + FormatDate(item.LastAt)
                : "追加日: " + FormatDate(item.AddedAt);

            // 曲名 (表示名) は折り返して全文表示する。行の高さは行数に合わせて伸ばす
            // テキスト幅 ≈ リスト幅 1040 - バッジ 118 - 削除ボタン 160
            int nameLines = UiFactory.EstimateWrapLines(item.Songfile, 29, 740f);
            float nameHeight = nameLines * UiFactory.LineHeight(29);
            // 2行目 (日付) の高さも FontScale 込みで確保する
            // (固定値だと大きい文字サイズ設定で Truncate が1行も描けず文字が消える)
            float subHeight = UiFactory.LineHeight(22);
            float rowHeight = Mathf.Max(20f + nameHeight + subHeight + 6f + 14f, 136f);

            var rowGo = new GameObject("Row");
            rowGo.transform.SetParent(_listContent, false);
            var img = rowGo.AddComponent<Image>();
            img.color = UiFactory.CardBg;
            UiFactory.Roundify(img);
            UiFactory.AddShadow(rowGo, 3f);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = rowHeight;
            var button = rowGo.AddComponent<Button>();
            rowGo.AddComponent<PressEffect>();

            // 左丸バッジ (履歴 = 歌った回数 / それ以外 = ♪)
            var circleGo = new GameObject("Badge");
            circleGo.transform.SetParent(rowGo.transform, false);
            var circleImg = circleGo.AddComponent<Image>();
            circleImg.sprite = UiFactory.RoundedSprite;
            circleImg.type = Image.Type.Sliced;
            circleImg.pixelsPerUnitMultiplier = 0.55f;
            circleImg.color = UiFactory.PrimaryPale;
            circleImg.raycastTarget = false;
            var circleRect = circleGo.GetComponent<RectTransform>();
            circleRect.anchorMin = circleRect.anchorMax = new Vector2(0f, 0.5f);
            circleRect.pivot = new Vector2(0f, 0.5f);
            circleRect.anchoredPosition = new Vector2(20f, 0f);
            circleRect.sizeDelta = new Vector2(78f, 78f);
            string mark = _tab == Tab.History ? item.Times.ToString() : "♪";
            var circleText = UiFactory.CreateText(circleGo.transform, "Mark", mark, 32, UiFactory.PrimaryDark);
            UiFactory.StretchFull(circleText.rectTransform);
            if (_tab == Tab.History)
            {
                var unit = UiFactory.CreateText(circleGo.transform, "Unit", "回", 16, UiFactory.PrimaryDark);
                var unitRect = unit.rectTransform;
                unitRect.anchorMin = unitRect.anchorMax = new Vector2(0.5f, 0f);
                unitRect.pivot = new Vector2(0.5f, 0f);
                unitRect.anchoredPosition = new Vector2(0f, 2f);
                unitRect.sizeDelta = new Vector2(60f, 20f);
            }

            var nameText = UiFactory.CreateText(rowGo.transform, "Name",
                UiFactory.NoWordWrap(item.Songfile), 29,
                UiFactory.TextDark, TextAnchor.UpperLeft);
            var nameRect = nameText.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.anchoredPosition = new Vector2(0f, -16f);
            nameRect.offsetMin = new Vector2(118f, nameRect.offsetMin.y);
            nameRect.offsetMax = new Vector2(-160f, nameRect.offsetMax.y);
            nameRect.sizeDelta = new Vector2(nameRect.sizeDelta.x, nameHeight);
            nameText.verticalOverflow = VerticalWrapMode.Overflow;

            var sub = UiFactory.CreateText(rowGo.transform, "Sub", line2, 22,
                UiFactory.TextMuted, TextAnchor.LowerLeft);
            var subRect = sub.rectTransform;
            subRect.anchorMin = new Vector2(0f, 0f);
            subRect.anchorMax = new Vector2(1f, 0f);
            subRect.pivot = new Vector2(0.5f, 0f);
            subRect.anchoredPosition = new Vector2(0f, 12f);
            subRect.offsetMin = new Vector2(118f, subRect.offsetMin.y);
            subRect.offsetMax = new Vector2(-160f, subRect.offsetMax.y);
            subRect.sizeDelta = new Vector2(subRect.sizeDelta.x, subHeight);
            sub.verticalOverflow = VerticalWrapMode.Truncate;

            if (_tab == Tab.History && _selectMode)
            {
                // 選択モード: 行タップで選択切替。右端は削除ボタンの代わりに選択マーク
                var check = UiFactory.CreateText(rowGo.transform, "Check", "○", 40, UiFactory.TextMuted);
                var checkRect = check.rectTransform;
                checkRect.anchorMin = checkRect.anchorMax = new Vector2(1f, 0.5f);
                checkRect.pivot = new Vector2(1f, 0.5f);
                checkRect.anchoredPosition = new Vector2(-36f, 0f);
                checkRect.sizeDelta = new Vector2(80f, 80f);
                System.Action applyState = () =>
                {
                    bool on = _selected.Contains(item.FullPath);
                    check.text = on ? "●" : "○";
                    check.color = on ? UiFactory.Primary : UiFactory.TextMuted;
                    img.color = on ? UiFactory.PrimaryPale : UiFactory.CardBg;
                };
                applyState();
                button.onClick.AddListener(() =>
                {
                    Se.Play(Se.Tap);
                    if (!_selected.Remove(item.FullPath))
                    {
                        _selected.Add(item.FullPath);
                    }
                    applyState();
                    UpdateHistoryTools();
                });
                _rows.Add(rowGo);
                return;
            }

            var entry = new ReserveScreen.Entry
            {
                Line1 = item.Songfile,
                Line2 = "",
                // TODO: ファイル移動に強くするなら /api/search.php で fullpath を再解決する
                Filename = ReserveScreen.BaseName(item.FullPath),
                FullPath = item.FullPath,
            };
            button.onClick.AddListener(() => ReserveScreen.Open(Manager, entry));

            // 削除 (2度押し確認)
            var deleteButton = UiFactory.CreateButton(rowGo.transform, "Delete", "削除",
                UiFactory.Danger, Color.white, 24);
            var delRect = deleteButton.GetComponent<RectTransform>();
            delRect.anchorMin = new Vector2(1f, 0.5f);
            delRect.anchorMax = new Vector2(1f, 0.5f);
            delRect.pivot = new Vector2(1f, 0.5f);
            delRect.anchoredPosition = new Vector2(-16f, 0f);
            delRect.sizeDelta = new Vector2(120f, 76f);
            var delLabel = deleteButton.GetComponentInChildren<Text>();
            bool armed = false;
            var tab = _tab;
            deleteButton.onClick.AddListener(async () =>
            {
                if (!armed)
                {
                    armed = true;
                    delLabel.text = "本当に？";
                    Se.Play(Se.Tap);
                    return;
                }
                try
                {
                    switch (tab)
                    {
                        case Tab.Later:
                            await MypageService.RemoveLaterAsync(item.FullPath);
                            break;
                        case Tab.Favorite:
                            await MypageService.RemoveFavoriteAsync(item.FullPath);
                            break;
                        default:
                            await MypageService.RemoveHistoryAsync(item.FullPath);
                            break;
                    }
                    Se.Play(Se.Confirm);
                }
                catch (System.Exception e)
                {
                    UiFactory.ShowToast("削除に失敗: " + e.Message, true);
                    Se.Play(Se.Error);
                }
                Reload();
            });

            _rows.Add(rowGo);
        }
    }
}
