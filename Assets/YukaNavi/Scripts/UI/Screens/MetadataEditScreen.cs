using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Api;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// 曲の情報 (曲名・歌手名・作品名・使われ方・補足説明 + 読み仮名) の修正画面。
    /// 予約確認画面の「曲の情報を修正する」から遷移し、「この内容で修正する」で
    /// 修正値を持って戻る。修正はこの時点ではサーバーへ送られず、予約の完了時に
    /// 予約行へ反映 + サーバーの修正ログ (ListerDB を直す材料) に記録される。
    /// </summary>
    public class MetadataEditScreen : ScreenBase
    {
        static SongMetadataDto _pendingInitial;
        static SongMetadataDto _result;

        /// <summary>Open されてから呼び出し元へ戻るまでの間 true (呼び出し元の復帰判定用)。</summary>
        public static bool HasSession { get; private set; }

        /// <summary>現在の曲情報を初期値にして修正画面を開く。</summary>
        public static void Open(ScreenManager manager, SongMetadataDto initial)
        {
            _pendingInitial = initial ?? new SongMetadataDto();
            _result = null;
            HasSession = true;
            manager.Show<MetadataEditScreen>();
        }

        /// <summary>
        /// 編集セッションを終了し、保存された修正値を返す (保存せず戻った場合は null)。
        /// </summary>
        public static SongMetadataDto EndSession()
        {
            HasSession = false;
            var result = _result;
            _result = null;
            return result;
        }

        SongMetadataDto _initial;
        InputField _songInput;
        InputField _songRubyInput;
        InputField _artistInput;
        InputField _artistRubyInput;
        InputField _workInput;
        InputField _workRubyInput;
        InputField _opEdInput;
        InputField _commentInput;

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            UiFactory.CreateTopBar(transform, "曲の情報を修正");

            // フォーム (スクロール)。下部は固定の保存ボタンの分を空ける
            var scrollRectT = UiFactory.CreateScrollList(transform, "Form", out var form);
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 132f);
            scrollRectT.offsetMax = new Vector2(-20f, -126f);

            // 説明
            const string guide = "曲の情報に誤りがあれば修正できます。修正は予約一覧の表示に"
                + "反映され、曲データベースの登録を直すための記録としても保存されます。";
            float guideH = UiFactory.EstimateWrapLines(guide, 24, 980f) * UiFactory.LineHeight(24);
            var guidePanel = AddPanel(form, guideH + 16f, transparent: true);
            var guideText = UiFactory.CreateText(guidePanel, "Guide", UiFactory.NoWordWrap(guide),
                24, UiFactory.TextMuted, TextAnchor.UpperLeft);
            UiFactory.StretchFull(guideText.rectTransform);
            guideText.rectTransform.offsetMin = new Vector2(8f, 0f);
            guideText.rectTransform.offsetMax = new Vector2(-8f, -8f);
            guideText.verticalOverflow = VerticalWrapMode.Overflow;

            const string rubyPlaceholder = "ひらがな・カタカナで入力";
            _songInput = AddInputRow(form, "曲名");
            _songRubyInput = AddInputRow(form, "曲名の読み", rubyPlaceholder);
            _artistInput = AddInputRow(form, "歌手名");
            _artistRubyInput = AddInputRow(form, "歌手名の読み", rubyPlaceholder);
            _workInput = AddInputRow(form, "作品名");
            _workRubyInput = AddInputRow(form, "作品名の読み", rubyPlaceholder);
            _opEdInput = AddInputRow(form, "使われ方", "OP / ED / 挿入歌 / ライブ披露曲 など");
            _commentInput = AddInputRow(form, "補足説明");

            // 保存 (下部固定、ナビバーの上)
            var saveButton = UiFactory.CreateButton(transform, "Save", "この内容で修正する",
                UiFactory.Primary, Color.white, 38);
            var saveRect = saveButton.GetComponent<RectTransform>();
            saveRect.anchorMin = new Vector2(0f, 0f);
            saveRect.anchorMax = new Vector2(1f, 0f);
            saveRect.pivot = new Vector2(0.5f, 0f);
            saveRect.anchoredPosition = new Vector2(0f, GlobalNav.BarHeight + 12f);
            saveRect.offsetMin = new Vector2(20f, saveRect.offsetMin.y);
            saveRect.offsetMax = new Vector2(-20f, saveRect.offsetMax.y);
            saveRect.sizeDelta = new Vector2(saveRect.sizeDelta.x, 108f);
            saveButton.onClick.AddListener(Save);
        }

        RectTransform AddPanel(RectTransform form, float height, bool transparent = false)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(form, false);
            var rect = go.AddComponent<RectTransform>();
            if (!transparent)
            {
                var img = go.AddComponent<Image>();
                img.color = UiFactory.CardBg;
                UiFactory.Roundify(img);
            }
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            return rect;
        }

        InputField AddInputRow(RectTransform form, string label, string placeholder = "")
        {
            float labelH = UiFactory.LineHeight(24);
            float inputH = Mathf.Max(78f, UiFactory.LineHeight(34) + 20f);
            var panel = AddPanel(form, 12f + inputH + 8f + labelH + 8f);

            var text = UiFactory.CreateText(panel, "Label", label, 24,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            var labelRect = text.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -8f);
            labelRect.offsetMin = new Vector2(24f, labelRect.offsetMin.y);
            labelRect.offsetMax = new Vector2(-24f, labelRect.offsetMax.y);
            labelRect.sizeDelta = new Vector2(labelRect.sizeDelta.x, labelH);

            var input = UiFactory.CreateInputField(panel, "Input", placeholder);
            var inputRect = input.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(1f, 0f);
            inputRect.pivot = new Vector2(0.5f, 0f);
            inputRect.anchoredPosition = new Vector2(0f, 12f);
            inputRect.offsetMin = new Vector2(24f, inputRect.offsetMin.y);
            inputRect.offsetMax = new Vector2(-24f, inputRect.offsetMax.y);
            inputRect.sizeDelta = new Vector2(inputRect.sizeDelta.x, inputH);
            return input;
        }

        public override void OnShow()
        {
            if (_pendingInitial != null)
            {
                _initial = _pendingInitial;
                _pendingInitial = null;
            }
            var meta = _initial ?? new SongMetadataDto();
            _songInput.text = meta.SongName ?? "";
            _songRubyInput.text = meta.SongRuby ?? "";
            _artistInput.text = meta.Artist ?? "";
            _artistRubyInput.text = meta.ArtistRuby ?? "";
            _workInput.text = meta.Work ?? "";
            _workRubyInput.text = meta.WorkRuby ?? "";
            _opEdInput.text = meta.OpEd ?? "";
            _commentInput.text = meta.Comment ?? "";
        }

        void Save()
        {
            // 前回のエラー表示を消す
            SetRubyErrorHighlight(_songRubyInput, false);
            SetRubyErrorHighlight(_artistRubyInput, false);
            SetRubyErrorHighlight(_workRubyInput, false);

            // 読み仮名は保存時にゆかりすたーの登録規則へそろえる。かな以外はエラー
            if (!NormalizeRubyField(_songRubyInput, _initial?.SongRuby,
                    "曲名の読み", out string songRuby))
            {
                return;
            }
            if (!NormalizeRubyField(_artistRubyInput, _initial?.ArtistRuby,
                    "歌手名の読み", out string artistRuby))
            {
                return;
            }
            if (!NormalizeRubyField(_workRubyInput, _initial?.WorkRuby,
                    "作品名の読み", out string workRuby))
            {
                return;
            }

            var edited = new SongMetadataDto
            {
                SongName = (_songInput.text ?? "").Trim(),
                SongRuby = songRuby,
                Artist = (_artistInput.text ?? "").Trim(),
                ArtistRuby = artistRuby,
                Work = (_workInput.text ?? "").Trim(),
                WorkRuby = workRuby,
                OpEd = (_opEdInput.text ?? "").Trim(),
                Comment = (_commentInput.text ?? "").Trim(),
            };
            if (edited.SameAs(_initial))
            {
                Se.Play(Se.Tap);
                UiFactory.ShowToast("変更はありませんでした");
                Manager.Back();
                return;
            }
            _result = edited;
            Se.Play(Se.Confirm);
            Manager.Back();
        }

        /// <summary>
        /// 読み欄の値を取り出して登録規則へそろえる。初期値から変更されていなければ
        /// そのまま通す (ListerDB の既存登録が規則外 [英字など] でも、触っていない欄は
        /// エラーにしない)。かな以外が含まれる変更は欄を赤くしてエラー表示し false。
        /// </summary>
        static bool NormalizeRubyField(InputField input, string initialValue,
                                       string label, out string result)
        {
            result = (input.text ?? "").Trim();
            if (result == (initialValue ?? "").Trim())
            {
                return true;
            }
            if (!TryNormalizeRuby(result, out string normalized))
            {
                SetRubyErrorHighlight(input, true);
                UiFactory.ShowToast(label + "に使えるのは ひらがな・カタカナ だけです", true);
                Se.Play(Se.Error);
                return false;
            }
            result = normalized;
            input.text = normalized; // 登録される形 (全角カタカナ・濁点なし) を見せる
            return true;
        }

        static void SetRubyErrorHighlight(InputField input, bool error)
        {
            input.image.color = error ? new Color(1f, 0.86f, 0.86f) : Color.white;
        }

        // ---- 読み仮名の正規化 (ゆかりすたーの登録規則: 全角カタカナ・濁点なし) ----

        // 半角カタカナ (U+FF66〜FF9D、コード順) と全角カタカナの対応
        const string HalfKana =
            "ｦｧｨｩｪｫｬｭｮｯｰｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜﾝ";
        const string HalfKanaFull =
            "ヲァィゥェォャュョッーアイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワン";
        // 濁音・半濁音と清音の対応
        const string Dakuon = "ガギグゲゴザジズゼゾダヂヅデドバビブベボパピプペポヴヷヸヹヺ";
        const string Seion = "カキクケコサシスセソタチツテトハヒフヘホハヒフヘホウワヰヱヲ";

        /// <summary>
        /// 読み仮名を「全角カタカナ・濁点/半濁点なし」へ正規化する
        /// (ひらがな・カタカナ・半角カタカナのどれで入力されてもそろえる)。
        /// かな以外 (英数字・漢字・記号) が含まれる場合は false。
        /// サーバー側 (api/song_metadata.php の normalize_ruby) と同じ規則。
        /// </summary>
        static bool TryNormalizeRuby(string input, out string result)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char raw in (input ?? "").Trim())
            {
                char c = raw;
                int half = HalfKana.IndexOf(c);
                if (half >= 0)
                {
                    c = HalfKanaFull[half];
                }
                else if (c == 'ﾞ' || c == 'ﾟ')
                {
                    continue; // 半角の濁点・半濁点は除去
                }
                if (c >= 'ぁ' && c <= 'ゖ')
                {
                    c = (char)(c + 0x60); // ひらがな → カタカナ
                }
                if (c == '゛' || c == '゜' || c == '゙' || c == '゚')
                {
                    continue; // 単独の濁点・半濁点は除去
                }
                int daku = Dakuon.IndexOf(c);
                if (daku >= 0)
                {
                    c = Seion[daku];
                }
                if ((c >= 'ァ' && c <= 'ヶ') || c == 'ー' || c == ' ' || c == '　')
                {
                    sb.Append(c);
                    continue;
                }
                result = null;
                return false; // かな以外
            }
            result = sb.ToString();
            return true;
        }
    }
}
