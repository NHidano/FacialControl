using System.Collections.Generic;

namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// binding が提供可能な gaze source を静的に宣言する値です。
    /// </summary>
    /// <remarks>
    /// <see cref="ChannelId"/> が null または空の場合は、広告駆動のように任意のチャネルを
    /// 提供できるワイルドカード宣言を表します。宣言は runtime 状態に依存しないため、
    /// Inspector が入力ソースの選択肢を列挙する際にも利用できます。
    /// </remarks>
    public readonly struct GazeSourceDeclaration
    {
        /// <summary>提供対象のチャネル id。null または空は全チャネルを表すワイルドカードです。</summary>
        public string ChannelId { get; }

        /// <summary>true の場合は左右 pair（.left/.right）を、false の場合は shared source を提供します。</summary>
        public bool ProvidesLeftRightPair { get; }

        public GazeSourceDeclaration(string channelId, bool providesLeftRightPair)
        {
            ChannelId = channelId;
            ProvidesLeftRightPair = providesLeftRightPair;
        }
    }

    /// <summary>
    /// binding が提供可能な gaze source を宣言する契約です。
    /// </summary>
    /// <remarks>
    /// この宣言を実装する provider を追加するだけで、将来の procedural gaze source も
    /// Gaze セクションの入力ソース選択肢へ拡張できます。宣言は静的で、runtime 状態に依存しません。
    /// </remarks>
    public interface IGazeSourceProvider
    {
        /// <summary>この binding が提供可能な gaze source の宣言を返します。</summary>
        IEnumerable<GazeSourceDeclaration> GetGazeSourceDeclarations();
    }

    /// <summary>
    /// Profile の Gaze セクションからチャネル id 列を binding へ注入する契約です。
    /// </summary>
    /// <remarks>
    /// <see cref="ConfigureGazeChannels"/> は rebuild ごと、binding の OnStart より前に呼び出されます。
    /// 引数は null ではなく、先頭が既定チャネル id <c>gaze</c> である不変条件済みのリストです。
    /// binding は次の rebuild まで参照を保持してよく、変更は再注入で通知されます。
    /// この型付き契約へ provider を追加するだけで、将来の procedural gaze source を同じ注入経路に載せられます。
    /// </remarks>
    public interface IGazeChannelConsumer
    {
        /// <summary>rebuild ごとに、OnStart 前の binding へ Gaze チャネル id を注入します。</summary>
        void ConfigureGazeChannels(IReadOnlyList<string> channelIds);
    }
}
