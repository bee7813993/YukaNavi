using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using YukaNavi.Api;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// ホーム画面。動画背景 + 音符パーティクル + ゆかりちゃん + 再生中ティッカー。
    /// 自分の予約の順番が近づくと「そろそろ出番」バナーを出す。
    /// </summary>
    public class HomeScreen : ScreenBase
    {
        const float PollIntervalSeconds = 5f;

        Text _tickerNowText;
        Text _tickerNextText;
        GameObject _banner;
        Text _bannerText;
        VideoPlayer _videoPlayer;
        RenderTexture _videoTexture;
        Coroutine _polling;
        bool _refreshing;

        public override void BuildUi()
        {
            BuildBackground();

            // 音符パーティクル (背景の上、マスコットの下)
            NoteParticles.Create(transform);

            // マスコット (グローバルナビバーの上に立つ)
            MascotView.Create(transform, new Vector2(740f, 1110f), GlobalNav.BarHeight + 20f);

            // ロゴ (上端中央、1800x520 と同比率)
            var logo = UiFactory.CreateImage(transform, "Logo", "Art/UI/yukanavi_logo");
            logo.preserveAspect = true;
            var logoRect = logo.rectTransform;
            logoRect.anchorMin = logoRect.anchorMax = new Vector2(0.5f, 1f);
            logoRect.pivot = new Vector2(0.5f, 1f);
            logoRect.anchoredPosition = new Vector2(0f, -40f);
            logoRect.sizeDelta = new Vector2(560f, 162f);

            // 再生中ティッカー (ロゴの下の半透明帯。いま/次 の2行構成)
            var tickerBg = UiFactory.CreatePanel(transform, "Ticker", new Color(1f, 1f, 1f, 0.72f));
            tickerBg.anchorMin = new Vector2(0f, 1f);
            tickerBg.anchorMax = new Vector2(1f, 1f);
            tickerBg.pivot = new Vector2(0.5f, 1f);
            tickerBg.anchoredPosition = new Vector2(0f, -212f);
            tickerBg.sizeDelta = new Vector2(0f, 96f);
            _tickerNowText = CreateTickerLine(tickerBg, "Now", -6f);
            _tickerNextText = CreateTickerLine(tickerBg, "Next", -48f);

            // そろそろ出番バナー (ティッカーの下)
            var banner = UiFactory.CreatePanel(transform, "Banner", UiFactory.Primary);
            banner.anchorMin = new Vector2(0f, 1f);
            banner.anchorMax = new Vector2(1f, 1f);
            banner.pivot = new Vector2(0.5f, 1f);
            banner.anchoredPosition = new Vector2(0f, -312f);
            banner.sizeDelta = new Vector2(-120f, 66f);
            _bannerText = UiFactory.CreateText(banner, "Text", "", 32, Color.white);
            UiFactory.StretchFull(_bannerText.rectTransform);
            _banner = banner.gameObject;
            _banner.SetActive(false);
        }

        /// <summary>背景。ループ動画があれば VideoPlayer で再生、無ければ静止画。</summary>
        void BuildBackground()
        {
            var clip = Resources.Load<VideoClip>("Videos/yukanavi_home_background_loop_rich_portrait_1080x1920");
            if (clip == null)
            {
                var bg = UiFactory.CreateImage(transform, "Background",
                    "Art/Backgrounds/yukanavi_home_background_no_character_1080x1920");
                UiFactory.StretchFull(bg.rectTransform);
                return;
            }

            _videoTexture = new RenderTexture((int)clip.width, (int)clip.height, 0);
            var videoGo = new GameObject("Background");
            videoGo.transform.SetParent(transform, false);
            var raw = videoGo.AddComponent<RawImage>();
            raw.texture = _videoTexture;
            raw.raycastTarget = false;
            UiFactory.StretchFull(raw.rectTransform);

            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.clip = clip;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _videoTexture;
            _videoPlayer.isLooping = true;
            _videoPlayer.playOnAwake = false;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        }

        public override void OnShow()
        {
            if (_videoPlayer != null)
            {
                _videoPlayer.Play();
            }
            _polling = StartCoroutine(PollRoutine());
        }

        public override void OnHide()
        {
            if (_videoPlayer != null)
            {
                _videoPlayer.Pause();
            }
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
            if (_refreshing)
            {
                return;
            }
            _refreshing = true;
            try
            {
                var client = AppConfig.CreateClient();
                var now = await client.GetNowPlayingAsync();
                var requests = await client.GetRequestsAsync();
                UpdateTicker(now, requests);
                UpdateTurnBanner(requests.Items);
            }
            catch (System.Exception e)
            {
                _tickerNowText.text = "未接続: " + e.Message;
                _tickerNextText.text = "";
                _banner.SetActive(false);
            }
            finally
            {
                _refreshing = false;
            }
        }

        /// <summary>ティッカーの1行分のテキストを作る (長い曲名は行内で切り詰め)。</summary>
        Text CreateTickerLine(RectTransform parent, string name, float y)
        {
            var text = UiFactory.CreateText(parent, name, "", 28, UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.offsetMin = new Vector2(24f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-24f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, 40f);
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        void UpdateTicker(NowPlayingDto now, RequestListDto requests)
        {
            if (now.Playing)
            {
                string nowLine = "♪ いま: " + now.PlayingTitle;
                if (!string.IsNullOrEmpty(now.PlayingSinger))
                {
                    nowLine += " (" + now.PlayingSinger + ")";
                }
                _tickerNowText.text = nowLine;
                _tickerNextText.text = now.NextSong != null ? "♪ 次: " + now.NextSong.Title : "♪ 次: (予約なし)";
            }
            else
            {
                _tickerNowText.text = "再生停止中";
                _tickerNextText.text = requests.RemainingCount > 0
                    ? "♪ 予約 " + requests.RemainingCount + " 件待ち"
                    : "♪ 予約を待っています";
            }
        }

        /// <summary>自分の予約 (名前一致) が未再生の先頭2件に入っていたらバナーを出す。</summary>
        void UpdateTurnBanner(List<RequestItemDto> items)
        {
            string username = AppConfig.Username;
            if (string.IsNullOrEmpty(username) || items == null)
            {
                _banner.SetActive(false);
                return;
            }
            // position 昇順 = 次に再生される順
            var pending = new List<RequestItemDto>();
            foreach (var item in items)
            {
                if (item.Nowplaying == "未再生" || item.Nowplaying == "1")
                {
                    pending.Add(item);
                }
            }
            pending.Sort((a, b) => a.Position.CompareTo(b.Position));
            int mine = pending.FindIndex(i => i.Singer == username);
            if (mine == 0)
            {
                _bannerText.text = "♪ 次はあなたの番だよ！";
                _banner.SetActive(true);
            }
            else if (mine == 1)
            {
                _bannerText.text = "♪ そろそろ出番！あと1曲だよ";
                _banner.SetActive(true);
            }
            else
            {
                _banner.SetActive(false);
            }
        }
    }
}
