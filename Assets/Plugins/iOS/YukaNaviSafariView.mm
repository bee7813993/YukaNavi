// アプリ内ブラウザ (SFSafariViewController)。Google ログインを外部ブラウザに出さない
// (App Store ガイドライン 4 対応)。C# 側は Assets/YukaNavi/Scripts/Core/InAppBrowser.cs。
// フローはポーリング型 (リダイレクト復帰なし) のため ASWebAuthenticationSession は使わない。

#import <SafariServices/SafariServices.h>
#import <UIKit/UIKit.h>

extern "C" UIViewController* UnityGetGLViewController();

@interface YukaNaviSafariDelegate : NSObject
    <SFSafariViewControllerDelegate, UIAdaptivePresentationControllerDelegate>
@end

static SFSafariViewController* _yukanaviSafari = nil;
static YukaNaviSafariDelegate* _yukanaviSafariDelegate = nil;
static BOOL _yukanaviSafariUserClosed = NO;

@implementation YukaNaviSafariDelegate

// ユーザーが「キャンセル」ボタンで閉じた (プログラムからの dismiss では呼ばれない)
- (void)safariViewControllerDidFinish:(SFSafariViewController*)controller
{
    if (controller == _yukanaviSafari) {
        _yukanaviSafari = nil;
        _yukanaviSafariUserClosed = YES;
    }
}

// iPad のシートを下スワイプで閉じた場合 (didFinish が呼ばれない経路)
- (void)presentationControllerDidDismiss:(UIPresentationController*)presentationController
{
    if (presentationController.presentedViewController == _yukanaviSafari) {
        _yukanaviSafari = nil;
        _yukanaviSafariUserClosed = YES;
    }
}

@end

static void YukaNaviSafariPresent(NSURL* url)
{
    UIViewController* root = UnityGetGLViewController();
    if (root == nil || url == nil) {
        // 開けなかったことを「閉じられた」として C# のポーリングに伝える
        // (伝えないと 5 分のポーリングを画面変化なしで待たせてしまう)
        _yukanaviSafariUserClosed = YES;
        return;
    }
    if (_yukanaviSafariDelegate == nil) {
        _yukanaviSafariDelegate = [YukaNaviSafariDelegate new];
    }
    SFSafariViewController* safari = [[SFSafariViewController alloc] initWithURL:url];
    safari.delegate = _yukanaviSafariDelegate;
    safari.dismissButtonStyle = SFSafariViewControllerDismissButtonStyleCancel;
    if (UI_USER_INTERFACE_IDIOM() == UIUserInterfaceIdiomPad) {
        // iPad は全画面より中央シートの方が自然。スワイプ閉じは presentationController 側で拾う
        safari.modalPresentationStyle = UIModalPresentationPageSheet;
    }
    safari.presentationController.delegate = _yukanaviSafariDelegate;
    // 万一 root が別のモーダルを出していても最前面から present する
    UIViewController* top = root;
    while (top.presentedViewController != nil) {
        top = top.presentedViewController;
    }
    _yukanaviSafari = safari;
    [top presentViewController:safari animated:YES completion:nil];
}

extern "C" {

// URL をアプリ内ブラウザで開く。既に開いていれば閉じてから開き直す (二重 present 防止)。
// http/https 以外や解釈できない URL は開かず 0 を返す (SFSafariViewController は
// http/https 以外の URL で NSInvalidArgumentException を投げてクラッシュするため)。
int YukaNaviSafariView_Open(const char* url)
{
    NSString* str = url ? [NSString stringWithUTF8String:url] : nil;
    NSURL* nsurl = str ? [NSURL URLWithString:str] : nil;
    NSString* scheme = nsurl.scheme.lowercaseString;
    if (nsurl == nil
        || !([scheme isEqualToString:@"http"] || [scheme isEqualToString:@"https"])) {
        return 0;
    }
    dispatch_async(dispatch_get_main_queue(), ^{
        _yukanaviSafariUserClosed = NO;
        SFSafariViewController* old = _yukanaviSafari;
        _yukanaviSafari = nil;
        if (old != nil && old.presentingViewController != nil) {
            [old.presentingViewController dismissViewControllerAnimated:NO completion:^{
                YukaNaviSafariPresent(nsurl);
            }];
        } else {
            YukaNaviSafariPresent(nsurl);
        }
    });
    return 1;
}

// アプリ内ブラウザを閉じる (開いていない・既に閉じられていれば何もしない)
void YukaNaviSafariView_Dismiss()
{
    dispatch_async(dispatch_get_main_queue(), ^{
        SFSafariViewController* safari = _yukanaviSafari;
        _yukanaviSafari = nil;
        if (safari != nil && safari.presentingViewController != nil) {
            [safari.presentingViewController dismissViewControllerAnimated:YES completion:nil];
        }
    });
}

// ユーザーが自分でシートを閉じたかを1回だけ返す (読むとリセット。C# のポーリングから呼ぶ)
int YukaNaviSafariView_ConsumeUserClosed()
{
    if (_yukanaviSafariUserClosed) {
        _yukanaviSafariUserClosed = NO;
        return 1;
    }
    return 0;
}

} // extern "C"
