using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// マイページ画面。履歴 / あとで歌う / お気に入り曲 の一覧を表示し、
    /// 行タップからもう一度予約できる。データはすべて端末ローカル (LocalMypage) に
    /// 保存されるため、サーバー設定に依存せずオフラインでも閲覧できる。
    /// </summary>
    public class MypageScreen : ScreenBase
    {
        enum Tab
        {
            History,
            Later,
            Favorite,
        }

        Tab _tab = Tab.History;
        Button _historyTab;
        Button _laterTab;
        Button _favoriteTab;
        Text _statusText;
        RectTransform _listContent;
        readonly List<GameObject> _rows = new List<GameObject>();

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            UiFactory.CreateTopBar(transform, "マイページ");

            // タブ (うたった曲 / あとで歌う / お気に入り)
            var tabBar = UiFactory.CreatePanel(transform, "Tabs");
            tabBar.anchorMin = new Vector2(0f, 1f);
            tabBar.anchorMax = new Vector2(1f, 1f);
            tabBar.pivot = new Vector2(0.5f, 1f);
            tabBar.anchoredPosition = new Vector2(0f, -118f);
            tabBar.offsetMin = new Vector2(20f, tabBar.offsetMin.y);
            tabBar.offsetMax = new Vector2(-20f, tabBar.offsetMax.y);
            tabBar.sizeDelta = new Vector2(tabBar.sizeDelta.x, 80f);
            var tabs = UiFactory.CreateSegmentTabs(tabBar,
                new[] { "うたった曲", "あとで歌う", "お気に入り" }, 28);
            _historyTab = tabs[0];
            _laterTab = tabs[1];
            _favoriteTab = tabs[2];
            _historyTab.onClick.AddListener(() => SetTab(Tab.History));
            _laterTab.onClick.AddListener(() => SetTab(Tab.Later));
            _favoriteTab.onClick.AddListener(() => SetTab(Tab.Favorite));

            _statusText = UiFactory.CreateText(transform, "Status", "", 28, UiFactory.TextDark);
            var statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -200f);
            statusRect.sizeDelta = new Vector2(-40f, 40f);

            var scrollRectT = UiFactory.CreateScrollList(transform, "MypageList", out _listContent);
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 16f);
            scrollRectT.offsetMax = new Vector2(-20f, -248f);

            UpdateTabColors();
        }

        public override void OnShow()
        {
            Reload();
        }

        void SetTab(Tab tab)
        {
            Se.Play(Se.Tap);
            if (_tab == tab)
            {
                return;
            }
            _tab = tab;
            UpdateTabColors();
            Reload();
        }

        void UpdateTabColors()
        {
            UiFactory.SetSegmentSelected(new[] { _historyTab, _laterTab, _favoriteTab }, (int)_tab);
        }

        void Reload()
        {
            foreach (var row in _rows)
            {
                Destroy(row);
            }
            _rows.Clear();

            List<LocalMypage.Item> items;
            switch (_tab)
            {
                case Tab.Later:
                    items = LocalMypage.GetLater();
                    break;
                case Tab.Favorite:
                    items = LocalMypage.GetFavorite();
                    break;
                default:
                    items = LocalMypage.GetHistory();
                    break;
            }

            if (items.Count == 0)
            {
                _statusText.text = _tab == Tab.History
                    ? "まだありません (予約すると記録されます)"
                    : "まだありません (予約画面から追加できます)";
                return;
            }
            _statusText.text = items.Count + " 件";
            foreach (var item in items)
            {
                AddRow(item);
            }
        }

        static string FormatDate(long unixtime)
        {
            return System.DateTimeOffset.FromUnixTimeSeconds(unixtime).ToLocalTime().ToString("M/d HH:mm");
        }

        void AddRow(LocalMypage.Item item)
        {
            string line2 = _tab == Tab.History
                ? "最終: " + FormatDate(item.LastAt)
                : "追加日: " + FormatDate(item.AddedAt);

            var rowGo = new GameObject("Row");
            rowGo.transform.SetParent(_listContent, false);
            var img = rowGo.AddComponent<Image>();
            img.color = UiFactory.CardBg;
            UiFactory.Roundify(img);
            UiFactory.AddShadow(rowGo, 3f);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 136f;
            var button = rowGo.AddComponent<Button>();
            rowGo.AddComponent<PressEffect>();
            var entry = new ReserveScreen.Entry
            {
                Line1 = item.Songfile,
                Line2 = "",
                // TODO: ファイル移動に強くするなら /api/search.php で fullpath を再解決する
                Filename = ReserveScreen.BaseName(item.FullPath),
                FullPath = item.FullPath,
            };
            button.onClick.AddListener(() => ReserveScreen.Open(Manager, entry));

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

            var nameText = UiFactory.CreateText(rowGo.transform, "Name", item.Songfile, 29,
                UiFactory.TextDark, TextAnchor.UpperLeft);
            UiFactory.StretchFull(nameText.rectTransform);
            nameText.rectTransform.offsetMin = new Vector2(118f, 46f);
            nameText.rectTransform.offsetMax = new Vector2(-160f, -12f);
            nameText.verticalOverflow = VerticalWrapMode.Truncate;

            var sub = UiFactory.CreateText(rowGo.transform, "Sub", line2, 22,
                UiFactory.TextMuted, TextAnchor.LowerLeft);
            UiFactory.StretchFull(sub.rectTransform);
            sub.rectTransform.offsetMin = new Vector2(118f, 12f);
            sub.rectTransform.offsetMax = new Vector2(-160f, -92f);
            sub.verticalOverflow = VerticalWrapMode.Truncate;

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
            deleteButton.onClick.AddListener(() =>
            {
                if (!armed)
                {
                    armed = true;
                    delLabel.text = "本当に？";
                    Se.Play(Se.Tap);
                    return;
                }
                switch (tab)
                {
                    case Tab.Later:
                        LocalMypage.RemoveLater(item.FullPath);
                        break;
                    case Tab.Favorite:
                        LocalMypage.RemoveFavorite(item.FullPath);
                        break;
                    default:
                        LocalMypage.RemoveHistory(item.FullPath);
                        break;
                }
                Se.Play(Se.Confirm);
                Reload();
            });

            _rows.Add(rowGo);
        }
    }
}
