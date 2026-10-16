using System.Text.RegularExpressions;
using VoidCat.Lookout;

namespace DGP.UnitySignals.Lookout {

    /// <summary>Teaches Lookout the UnitySignals pattern: each signal field is an event, SetValue / a ".Value ="
    /// write / a transaction's Set raises it, AddObserver listens and RemoveObserver stops listening.
    /// <para>Lookout finds it through TypeCache: no registration or setting. It replaces the "Unity Signals" custom
    /// bus card (remove the card, or each SetValue and AddObserver shows twice). Source text only, so it doesn't
    /// reference the package.</para></summary>
    public sealed class UnitySignalsScanner : IEventGraphScanner {

        private const string BUS_NAME = "Unity Signals";

        // "IntegerValueSignal Health =", "ReferenceSignal<Transform> Target =", "ComputedSignal<bool> IsDead {"
        private static readonly Regex RE_DECL = new(
            @"^\s*(?:(?:public|private|protected|internal|readonly|static)\s+)*(\w*Signal(?:<[^;={]+?>)?)\s+(\w+)\s*[=;{]",
            RegexOptions.Compiled);
        private static readonly Regex RE_SET_VALUE   = new(@"(\w+)\.SetValue\(",                RegexOptions.Compiled);
        private static readonly Regex RE_VALUE_WRITE = new(@"(\w+)\.Value\s*[-+*/]?=(?!=)",      RegexOptions.Compiled);
        private static readonly Regex RE_TX_SET      = new(@"\.Set\(\s*(\w+)\s*,",              RegexOptions.Compiled);
        private static readonly Regex RE_OBSERVE     = new(@"(\w+)\.(Add|Remove)Observer\(\s*(\w+)?", RegexOptions.Compiled);
        private static readonly Regex RE_NAMESPACE   = new(@"^\s*namespace\s+([\w.]+)",          RegexOptions.Compiled);
        private static readonly Regex RE_CLASS       = new(
            @"^\s*(?:(?:public|private|protected|internal|sealed|abstract|static|partial)\s+)*(?:class|struct)\s+(\w+)",
            RegexOptions.Compiled);
        private static readonly Regex RE_METHOD      = new(
            @"^\s*(?:(?:public|private|protected|internal|static|virtual|override|async)\s+)*"
          + @"(?!(?:return|new|await|throw|else|case|using)\b)[\w<>\[\],.?]+\s+(\w+)\s*\(",
            RegexOptions.Compiled);

        public EventsScanResult Scan(string filePath, string fileContent, LookoutConfig config) {
            // Runs on every file: leave the ones that don't use the library at once.
            if (!fileContent.Contains("DGP.UnitySignals")) return null;

            var result = new EventsScanResult();
            var lines  = fileContent.Split('\n');
            string ns = string.Empty, cls = string.Empty, method = string.Empty;

            for (var i = 0; i < lines.Length; i++) {
                var line    = lines[i];
                var lineNo  = i + 1;
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*")) continue;

                var m = RE_NAMESPACE.Match(line); if (m.Success) ns  = m.Groups[1].Value;
                m = RE_CLASS.Match(line);         if (m.Success) cls = m.Groups[1].Value;
                m = RE_METHOD.Match(line);        if (m.Success) method = m.Groups[1].Value;

                // A signal field or property: the event itself.
                m = RE_DECL.Match(line);
                if (m.Success) {
                    result.Declarations.Add(new RawEventDecl {
                        EventTypeName  = m.Groups[2].Value,   // the field name is the event's identity
                        DelegateType   = m.Groups[1].Value,   // the signal type, as the node's port label
                        Namespace      = ns,
                        FilePath       = filePath,
                        LineNumber     = lineNo,
                        DeclaringClass = cls,
                        Category       = EventCategory.CustomBus,
                        BusName        = BUS_NAME,
                    });
                    continue;
                }

                // Raises: SetValue on any receiver; a ".Value =" or a transaction Set only on a declared signal.
                foreach (Match r in RE_SET_VALUE.Matches(line))   AddRaise(result, r.Groups[1].Value, false);
                foreach (Match r in RE_VALUE_WRITE.Matches(line)) AddRaise(result, r.Groups[1].Value, true);
                foreach (Match r in RE_TX_SET.Matches(line))      AddRaise(result, r.Groups[1].Value, true);

                // Listening and stopping.
                foreach (Match s in RE_OBSERVE.Matches(line)) {
                    var handler = s.Groups[3].Success ? s.Groups[3].Value : null;
                    result.SubscribeSites.Add(new RawSubscribeSite {
                        EventTypeName   = s.Groups[1].Value,
                        Namespace       = ns,
                        FilePath        = filePath,
                        LineNumber      = lineNo,
                        EnclosingClass  = cls,
                        EnclosingMethod = method,
                        BoundMethodName = handler,
                        IsLambda        = handler == null,
                        IsUnsubscribe   = s.Groups[2].Value == "Remove",
                        BusName         = BUS_NAME,
                    });
                }

                void AddRaise(EventsScanResult into, string signal, bool requiresDeclaration) =>
                    into.RaiseSites.Add(new RawRaiseSite {
                        EventTypeName       = signal,
                        Namespace           = ns,
                        FilePath            = filePath,
                        LineNumber          = lineNo,
                        EnclosingClass      = cls,
                        EnclosingMethod     = method,
                        BusName             = BUS_NAME,
                        RequiresDeclaration = requiresDeclaration,
                    });
            }
            return result;
        }
    }
}
