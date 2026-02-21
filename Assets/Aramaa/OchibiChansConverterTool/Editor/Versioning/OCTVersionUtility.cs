#if UNITY_EDITOR
// Assets/Aramaa/OchibiChansConverterTool/Editor/Utilities/OCTVersionUtility.cs

using System;
using UnityEngine.Networking;

namespace Aramaa.OchibiChansConverterTool.Editor.Utilities
{
    // -----------------------------------------------------------------------------
    // 概要
    // - このファイルの責務:
    //   - 最新版文字列の取得（HTTP GET）と結果通知を行います。
    //   - 取得後のバージョン比較処理を VersionComparer へ委譲します。
    // - このファイルがしないこと:
    //   - JSON パースの詳細実装、バージョン比較の詳細実装は保持しません。
    // - 主要な入出力:
    //   - 入力: latest.json の URL、完了コールバック、現在版/最新版文字列。
    //   - 出力: OCTVersionFetchResult / OCTVersionStatus。
    //   - エラー処理: 通信・抽出失敗は失敗結果として返し、例外は外に投げません。
    // - 想定ユースケース:
    //   - エディタ UI から非同期で最新版確認を実行し、更新通知表示に利用する。
    //
    // 重要メモ（初心者向け）
    // - キャッシュ回避のため、URL に時刻ベースのクエリ（t=Ticks）を付与します。
    // - URL は絶対 HTTP/HTTPS のみ受け付け、その他スキームは失敗として扱います。
    //   ※この URL は利用者入力ではなく、開発側が設定する配布先設定を想定しています。
    // - 完了イベント内で request.Dispose() を finally で必ず実行します。
    //   そのため途中 return があっても解放漏れしません。
    // - onComplete は null 許容です（null 条件演算子で呼び出し）。
    // - 抽出処理は OCTLatestJsonParser へ委譲され、
    //   VCC 配布制約上、外部 JSON 管理ライブラリには依存しません。
    //
    // チーム開発向けルール
    // - 失敗を例外ではなく結果型（Failure）で返す現在方針を維持してください。
    // - 通信オブジェクトの破棄（Dispose）を必ず維持してください。
    // - ローカライズメッセージキー（Version.*）を変更する場合は呼び出し元表示も確認してください。
    // -----------------------------------------------------------------------------

    internal enum OCTVersionStatus
    {
        Unknown,
        UpToDate,
        UpdateAvailable,
        Ahead
    }

    /// <summary>
    /// バージョンチェック全体のオーケストレーション（通信・結果返却）を担当します。
    /// </summary>
    internal static class OCTVersionUtility
    {
        /// <summary>
        /// 指定 URL から最新版情報を非同期取得し、完了時に結果を通知します。
        /// </summary>
        /// <param name="url">
        /// latest.json の取得先 URL。
        /// null/空白、または絶対 HTTP/HTTPS URL ではない場合は通信せず失敗結果を返します。
        /// </param>
        /// <param name="onComplete">
        /// 取得完了時のコールバック。
        /// null の場合は通知を行いません。
        /// </param>
        /// <remarks>
        /// 副作用として HTTP 通信を実行します。
        /// 通信失敗・JSON 解析失敗は Failure 結果で返し、例外は送出しません。
        /// </remarks>
        public static void FetchLatestVersionAsync(string url, Action<OCTVersionFetchResult> onComplete)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                onComplete?.Invoke(OCTVersionFetchResult.Failure(L("Version.ErrorMissingUrl")));
                return;
            }

            if (!IsSupportedHttpUrl(url))
            {
                onComplete?.Invoke(OCTVersionFetchResult.Failure(L("Version.ErrorInvalidUrl")));
                return;
            }

            var requestUrl = AppendCacheBuster(url);
            var request = UnityWebRequest.Get(requestUrl);
            request.timeout = 10;

            var operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                try
                {
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        onComplete?.Invoke(OCTVersionFetchResult.Failure(request.error));
                        return;
                    }

                    var latestVersion = OCTLatestJsonParser.ExtractLatestVersion(request.downloadHandler.text);
                    if (string.IsNullOrWhiteSpace(latestVersion))
                    {
                        onComplete?.Invoke(OCTVersionFetchResult.Failure(L("Version.ExtractFailed")));
                        return;
                    }

                    onComplete?.Invoke(OCTVersionFetchResult.Success(latestVersion));
                }
                finally
                {
                    request.Dispose();
                }
            };
        }

        /// <summary>
        /// 現在版と最新版（安定版優先・必要時プレリリース）との関係を判定します。
        /// </summary>
        /// <param name="currentVersion">現在版のバージョン文字列。</param>
        /// <param name="latestVersion">最新版のバージョン文字列。</param>
        /// <returns>
        /// 判定結果。
        /// 解析不能時は <see cref="OCTVersionStatus.Unknown"/>。
        /// </returns>
        public static OCTVersionStatus GetVersionStatus(string currentVersion, string latestVersion)
        {
            return OCTVersionComparer.GetVersionStatus(currentVersion, latestVersion);
        }

        private static string AppendCacheBuster(string url)
        {
            var separator = url.Contains("?") ? "&" : "?";
            var cacheBuster = DateTime.UtcNow.Ticks;
            return $"{url}{separator}t={cacheBuster}";
        }

        private static bool IsSupportedHttpUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        private static string L(string key) => OCTLocalization.Get(key);
    }

    internal readonly struct OCTVersionFetchResult
    {
        private OCTVersionFetchResult(string latestVersion, string error)
        {
            LatestVersion = latestVersion;
            Error = error;
        }

        public string LatestVersion { get; }
        public string Error { get; }
        public bool Succeeded => string.IsNullOrWhiteSpace(Error);

        /// <summary>
        /// 最新バージョン文字列を保持した成功結果を生成します。
        /// </summary>
        /// <param name="latestVersion">取得できた最新バージョン文字列。</param>
        /// <returns>成功を表す結果。</returns>
        public static OCTVersionFetchResult Success(string latestVersion)
        {
            return new OCTVersionFetchResult(latestVersion, null);
        }

        /// <summary>
        /// エラー内容を保持した失敗結果を生成します。
        /// </summary>
        /// <param name="error">失敗理由を表すメッセージ。</param>
        /// <returns>失敗を表す結果。</returns>
        public static OCTVersionFetchResult Failure(string error)
        {
            return new OCTVersionFetchResult(null, error);
        }
    }
}
#endif
