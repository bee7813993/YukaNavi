using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// マイページ画面。履歴 / あとで歌う / お気に入り曲 / お気に入り検索 の一覧を表示し、
    /// 行タップからもう一度予約 (検索はタップで実行) できる。
    /// 未リンク時は端末ローカル (LocalMypage)、サーバー連携 (デバイスリンク) 済みなら
    /// Web 版と同じサーバーデータを表示する (MypageService が振り分ける)。
    /// </summary>
    public class MypageScreen : ScreenBase
    {
        enum Tab
        {
            History,
            Later,
            Favorite,
            Search,
        }

        static int _pendingTab = -1;

        /// <summary>タブを指定して開く (0=うたった曲 1=あとで歌う 2=お気に入り曲 3=お気に入り検索)。検索トップの動線用。</summary>
        public static void Open(ScreenManager manager, int tab)
        {
            _pendingTab = tab;
            manager.Show<MypageScreen>();
        }

        Tab _tab = Tab.History;
        Button[] _tabs;
        Text _statusText;
        RectTransform _listContent;
        readonly List<GameObject> _rows = new List<GameObject>();
        int _reloadSerial;
        Button _linkButton;
        Text _linkButtonLabel;

        public override void BuildUi()
        {
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

            // タブ (うたった曲 / あとで歌う / お気に入り曲 / お気に入り検索)
            var tabBar = UiFactory.CreatePanel(transform, "Tabs");
            tabBar.anchorMin = new Vector2(0f, 1f);
            tabBar.anchorMax = new Vector2(1f, 1f);
            tabBar.pivot = new Vector2(0.5f, 1f);
            tabBar.anchoredPosition = new Vector2(0f, -118f);
            tabBar.offsetMin = new Vector2(20f, tabBar.offsetMin.y);
            tabBar.offsetMax = new Vector2(-20f, tabBar.offsetMax.y);
            tabBar.sizeDelta = new Vector2(tabBar.sizeDelta.x, 80f);
            _tabs = UiFactory.CreateSegmentTabs(tabBar,
                new[] { "うたった曲", "あとで歌う", "お気に入り曲", "お気に入り検索" }, 23);
            for (int i = 0; i < _tabs.Length; i++)
            {
                var tab = (Tab)i;
                _tabs[i].onClick.AddListener(() => SetTab(tab));
            }

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
            if (_pendingTab >= 0 && _pendingTab <= 3)
            {
                _tab = (Tab)_pendingTab;
                _pendingTab = -1;
                UpdateTabColors();
            }
            UpdateLinkButton(); // 連携画面から戻ったときに状態を反映
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
            UpdateTabColors();
            Reload();
        }

        void UpdateTabColors()
        {
            UiFactory.SetSegmentSelected(_tabs, (int)_tab);
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
            if (MypageService.IsLinked || MypageService.HasGoogleCarry)
            {
                ShowLoading();
            }

            var tab = _tab;
            try
            {
                // 未リンクでも Google 同期を持ち歩いていれば、この部屋への自動リンクを試みる
                if (!MypageService.IsLinked && MypageService.HasGoogleCarry)
                {
                    bool linked = await MypageService.TryGoogleAutoLinkAsync();
                    if (serial != _reloadSerial)
                    {
                        return;
                    }
                    if (linked)
                    {
                        UpdateLinkButton();
                        UiFactory.ShowToast("Google アカウントで同期を開始しました");
                    }
                    else if (!MypageService.HasGoogleCarry)
                    {
                        // 自動リンク中にトークン無効が判明して持ち歩きが破棄された
                        UiFactory.ShowToast("Google の認証が切れています。連携をやり直してください", true);
                    }
                }
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
            float rowHeight = Mathf.Max(20f + nameHeight + 36f + 14f, 136f);

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
            subRect.sizeDelta = new Vector2(subRect.sizeDelta.x, 30f);
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
