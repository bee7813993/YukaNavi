using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Api;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// 期別リスト / 年代別リスト画面。期別はアニメの1クール (3ヶ月) ごとに
    /// 「年 → 期 → 作品」、年代別 (OpenYearly) は「年 → 1年ぶんの作品」(quarter=0) と辿り、
    /// 作品をタップすると検索結果画面 (作品の完全一致) で曲一覧を出す。
    /// 期の判別は ListerDB のタイアップのリリース日 (ゆかりすたー本家と同じ基準)。
    /// ナビの「戻る」で階層を1つずつ遡る。
    /// </summary>
    public class PeriodScreen : ScreenBase
    {
        enum Level
        {
            Years,
            Quarters,
            Programs,
        }

        static bool _resetPending;
        static bool _pendingYearly;

        /// <summary>年代別モード (年 → 1年ぶんの作品一覧。期の階層を挟まない)</summary>
        bool _yearly;
        Level _level = Level.Years;
        int _year;
        int _quarter;
        Text _titleText;
        Text _breadcrumbText;
        Text _statusText;
        RectTransform _listContent;
        GameObject _quarterNav;
        Button _prevButton;
        Button _nextButton;
        Text _quarterNavLabel;
        readonly List<GameObject> _rows = new List<GameObject>();
        int _loadSerial;

        /// <summary>期別リストを最初 (年一覧) から開く。</summary>
        public static void Open(ScreenManager manager)
        {
            _resetPending = true;
            _pendingYearly = false;
            manager.Show<PeriodScreen>();
        }

        /// <summary>年代別リスト (年 → 1年ぶんの作品一覧) として開く。</summary>
        public static void OpenYearly(ScreenManager manager)
        {
            _resetPending = true;
            _pendingYearly = true;
            manager.Show<PeriodScreen>();
        }

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            var topBar = UiFactory.CreateTopBar(transform, "期別リスト");
            _titleText = topBar.GetComponentInChildren<Text>();

            // 現在位置 (パンくず)
            _breadcrumbText = UiFactory.CreateText(transform, "Breadcrumb", "", 26,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            var crumbRect = _breadcrumbText.rectTransform;
            crumbRect.anchorMin = new Vector2(0f, 1f);
            crumbRect.anchorMax = new Vector2(1f, 1f);
            crumbRect.pivot = new Vector2(0.5f, 1f);
            crumbRect.anchoredPosition = new Vector2(0f, -122f);
            crumbRect.offsetMin = new Vector2(24f, crumbRect.offsetMin.y);
            crumbRect.offsetMax = new Vector2(-24f, crumbRect.offsetMax.y);
            crumbRect.sizeDelta = new Vector2(crumbRect.sizeDelta.x, 40f);

            // 前の期 / 次の期 ナビ (作品一覧のときだけ表示)
            var nav = UiFactory.CreatePanel(transform, "QuarterNav");
            nav.anchorMin = new Vector2(0f, 1f);
            nav.anchorMax = new Vector2(1f, 1f);
            nav.pivot = new Vector2(0.5f, 1f);
            nav.anchoredPosition = new Vector2(0f, -170f);
            nav.offsetMin = new Vector2(20f, nav.offsetMin.y);
            nav.offsetMax = new Vector2(-20f, nav.offsetMax.y);
            nav.sizeDelta = new Vector2(nav.sizeDelta.x, 76f);

            _prevButton = UiFactory.CreateSoftButton(nav, "Prev", "◀ 前の期", 26);
            var prevRect = _prevButton.GetComponent<RectTransform>();
            prevRect.anchorMin = new Vector2(0f, 0.5f);
            prevRect.anchorMax = new Vector2(0f, 0.5f);
            prevRect.pivot = new Vector2(0f, 0.5f);
            prevRect.anchoredPosition = Vector2.zero;
            prevRect.sizeDelta = new Vector2(240f, 72f);
            _prevButton.onClick.AddListener(() => MoveQuarter(-1));

            _quarterNavLabel = UiFactory.CreateText(nav, "Label", "", 28, UiFactory.TextDark);
            UiFactory.StretchFull(_quarterNavLabel.rectTransform);
            _quarterNavLabel.rectTransform.offsetMin = new Vector2(250f, 0f);
            _quarterNavLabel.rectTransform.offsetMax = new Vector2(-250f, 0f);

            _nextButton = UiFactory.CreateSoftButton(nav, "Next", "次の期 ▶", 26);
            var nextRect = _nextButton.GetComponent<RectTransform>();
            nextRect.anchorMin = new Vector2(1f, 0.5f);
            nextRect.anchorMax = new Vector2(1f, 0.5f);
            nextRect.pivot = new Vector2(1f, 0.5f);
            nextRect.anchoredPosition = Vector2.zero;
            nextRect.sizeDelta = new Vector2(240f, 72f);
            _nextButton.onClick.AddListener(() => MoveQuarter(+1));

            _quarterNav = nav.gameObject;
            _quarterNav.SetActive(false);

            _statusText = UiFactory.CreateText(transform, "Status", "", 26, UiFactory.TextDark);
            var statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -254f);
            statusRect.sizeDelta = new Vector2(-40f, 36f);

            var scrollRectT = UiFactory.CreateScrollList(transform, "IndexList", out _listContent);
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 16f);
            scrollRectT.offsetMax = new Vector2(-20f, -298f);
        }

        public override void OnShow()
        {
            if (_resetPending)
            {
                _resetPending = false;
                _yearly = _pendingYearly;
                _titleText.text = _yearly ? "年代別リスト" : "期別リスト";
                _ = ShowYearsAsync();
            }
            else if (_rows.Count == 0)
            {
                _ = ReloadCurrentAsync();
            }
        }

        /// <summary>ナビの「戻る」: 階層を1つ戻る (年一覧まで来ていたら画面ごと戻る)。</summary>
        public override bool OnBackRequested()
        {
            switch (_level)
            {
                case Level.Programs:
                    if (_yearly)
                    {
                        _ = ShowYearsAsync(); // 年代別は期の階層を挟まない
                    }
                    else
                    {
                        _ = ShowQuartersAsync(_year);
                    }
                    return true;
                case Level.Quarters:
                    _ = ShowYearsAsync();
                    return true;
                default:
                    return false;
            }
        }

        Task ReloadCurrentAsync()
        {
            switch (_level)
            {
                case Level.Programs:
                    return ShowProgramsAsync(_year, _quarter);
                case Level.Quarters:
                    return ShowQuartersAsync(_year);
                default:
                    return ShowYearsAsync();
            }
        }

        /// <summary>前の期 / 次の期へ移動する (年またぎ対応)。年全体ビューでは年単位で移動。</summary>
        void MoveQuarter(int direction)
        {
            Se.Play(Se.Tap);
            int quarter = _quarter;
            int year = _year;
            if (quarter == 0)
            {
                year += direction;
            }
            else
            {
                quarter += direction;
                if (quarter < 1)
                {
                    quarter = 4;
                    year--;
                }
                else if (quarter > 4)
                {
                    quarter = 1;
                    year++;
                }
            }
            _ = ShowProgramsAsync(year, quarter);
        }

        void SetStatus(string message, bool isError)
        {
            HideLoading(); // 結果・エラーの表示 = ローディング終了
            _statusText.text = message;
            _statusText.color = isError ? UiFactory.Danger : UiFactory.TextDark;
        }

        void ClearRows()
        {
            foreach (var row in _rows)
            {
                Destroy(row);
            }
            _rows.Clear();
        }

        static string QuarterLabel(int quarter)
        {
            switch (quarter)
            {
                case 0:
                    return "1年すべて";
                case 1:
                    return "1月〜3月：冬";
                case 2:
                    return "4月〜6月：春";
                case 3:
                    return "7月〜9月：夏";
                case 4:
                    return "10月〜12月：秋";
                default:
                    return "";
            }
        }

        // ---- 年一覧 ----

        async Task ShowYearsAsync()
        {
            _level = Level.Years;
            _breadcrumbText.text = "リリース年をえらんでください";
            _quarterNav.SetActive(false);
            int serial = ++_loadSerial;
            SetStatus("", false);
            ShowLoading();
            ClearRows();
            ListerYearsDto data;
            try
            {
                data = await AppConfig.CreateClient().GetListerYearsAsync();
            }
            catch (System.Exception e)
            {
                if (serial == _loadSerial)
                {
                    SetStatus("読み込みに失敗: " + e.Message, true);
                    Se.Play(Se.Error);
                }
                return;
            }
            if (serial != _loadSerial || _level != Level.Years)
            {
                return;
            }
            if (data.Years == null || data.Years.Count == 0)
            {
                SetStatus("リリース日が登録された曲がありません", false);
                return;
            }
            SetStatus("", false);
            foreach (var year in data.Years)
            {
                int y = year.Year;
                if (_yearly)
                {
                    AddIndexRow($"{y} 年", $"{year.Songs} 曲", () => _ = ShowProgramsAsync(y, 0));
                }
                else
                {
                    AddIndexRow($"{y} 年", $"{year.Songs} 曲", () => _ = ShowQuartersAsync(y));
                }
            }
        }

        // ---- 期一覧 ----

        async Task ShowQuartersAsync(int year)
        {
            _level = Level.Quarters;
            _year = year;
            _breadcrumbText.text = $"期別 ＞ {year} 年";
            _quarterNav.SetActive(false);
            int serial = ++_loadSerial;
            SetStatus("", false);
            ShowLoading();
            ClearRows();
            ListerQuartersDto data;
            try
            {
                data = await AppConfig.CreateClient().GetListerQuartersAsync(year);
            }
            catch (System.Exception e)
            {
                if (serial == _loadSerial)
                {
                    SetStatus("読み込みに失敗: " + e.Message, true);
                    Se.Play(Se.Error);
                }
                return;
            }
            if (serial != _loadSerial || _level != Level.Quarters)
            {
                return;
            }
            if (data.Quarters == null || data.Quarters.Count == 0)
            {
                SetStatus("この年の曲がありません", false);
                return;
            }
            SetStatus("", false);
            foreach (var quarter in data.Quarters)
            {
                int q = quarter.Q;
                AddIndexRow(quarter.Label, $"{quarter.Programs} 作品・{quarter.Songs} 曲",
                    () => _ = ShowProgramsAsync(year, q));
            }
        }

        // ---- 作品一覧 ----

        async Task ShowProgramsAsync(int year, int quarter)
        {
            _level = Level.Programs;
            _year = year;
            _quarter = quarter;
            _breadcrumbText.text = _yearly
                ? $"年代別 ＞ {year} 年"
                : $"期別 ＞ {year} 年 ＞ {QuarterLabel(quarter)}";
            _quarterNav.SetActive(true);
            _quarterNavLabel.text = quarter == 0 ? $"{year} 年" : $"{year} 年　{QuarterLabel(quarter)}";
            _prevButton.GetComponentInChildren<Text>().text = quarter == 0 ? "◀ 前の年" : "◀ 前の期";
            _nextButton.GetComponentInChildren<Text>().text = quarter == 0 ? "次の年 ▶" : "次の期 ▶";
            int serial = ++_loadSerial;
            SetStatus("", false);
            ShowLoading();
            ClearRows();
            ListerProgramsDto data;
            try
            {
                data = await AppConfig.CreateClient().GetListerProgramsAsync(year, quarter);
            }
            catch (System.Exception e)
            {
                if (serial == _loadSerial)
                {
                    SetStatus("読み込みに失敗: " + e.Message, true);
                    Se.Play(Se.Error);
                }
                return;
            }
            if (serial != _loadSerial || _level != Level.Programs)
            {
                return;
            }
            if (data.Programs == null || data.Programs.Count == 0)
            {
                SetStatus("この期の曲がありません (前後の期へ移動できます)", false);
                return;
            }
            SetStatus($"{data.Programs.Count} 作品", false);
            foreach (var program in data.Programs)
            {
                string name = program.Program;
                string sub = $"{program.Songs} 曲";
                if (!string.IsNullOrEmpty(program.Group))
                {
                    sub += "　／　" + program.Group + "シリーズ";
                }
                AddIndexRow(name, sub,
                    () => SearchResultScreen.OpenExact(Manager, "program", name));
            }
        }

        /// <summary>インデックス共通の行 (タイトル + サブテキスト)。</summary>
        void AddIndexRow(string title, string sub, System.Action onTap)
        {
            _rows.Add(UiFactory.CreateIndexRow(_listContent, title, sub, () =>
            {
                Se.Play(Se.Tap);
                onTap();
            }));
        }
    }
}
