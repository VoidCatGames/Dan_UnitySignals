# Using Unity Signals with Lookout

Lookout (part of VoidCat Studios' CodeCompass suite for Unity) draws a project's events as a graph:
who raises each event, who listens, and what isn't cleaned up. This guide shows how to make it show a project that uses
Unity Signals (package `unitysignals`). Lookout knows nothing about this library on its own: it doesn't connect a signal with its
setters and observers. This guide shows three ways to teach it, from least to most work:

| Way | Graph | Live pulses and Trace | Effort |
|---|---|---|---|
| A. A **custom bus** in Settings | `SetValue` raises, `AddObserver` listens | no | a few fields, no code |
| B. A **dedicated scanner** (`IEventGraphScanner`, in `Documentation~/Lookout/`) | also `.Value =` and transactions | no | one editor script |
| C. **Lookout's attributes** on your code | what you mark | yes | an attribute per raise and listener |

A or B gives the graph. For pulses, add C (or the hook in section 5) to the signals you want to watch at runtime.

## 1. Install the library

Window ▸ Package Manager ▸ **+** ▸ **Install package from git URL**, and enter
`https://github.com/VoidCatGames/Dan_UnitySignals.git`.

Then point Lookout at your code. Open Project Settings ▸ CodeCompass ▸ Lookout and add your folders under
**Scanning ▸ Included Folders**.

## 2. The model: a signal is an event

Lookout's graph has events, the code that raises them, and the code that listens. For UnitySignals:

- **The event** is the signal field or property: `Health` in `public readonly IntegerValueSignal Health`.
- **A raise** is anything that changes its value: `Health.SetValue(…)`, `Health.Value = …` / `+=`, and
  `transaction.Set(Health, …)`.
- **A listener** is `Health.AddObserver(Handler)`. `Health.RemoveObserver(Handler)` stops it.

Events are named by the signal's field name. So `Player.Health.AddObserver(…)` in another class lands on the same
`Health` node as the `Health` field. Two different signals with the same field name share one node.

## 3. Way A: a custom bus (no code)

1. Open Project Settings ▸ CodeCompass ▸ Lookout ▸ **Scanning ▸ Custom Buses**.
2. Press **+ Custom Bus** and fill in the card:

| Field | Value | Why |
|---|---|---|
| Bus Name | `Unity Signals` | the name on the filter chip and in node details |
| Event Type | *(empty)* | match by the method names only |
| Publish Method | `SetValue` | `Health.SetValue(5)` raises `Health` |
| Subscribe Method | `AddObserver` | `Health.AddObserver(OnHealth)` listens to `Health` |
| Unsubscribe Method | `RemoveObserver` | marks the listener as cleaned up (no LEAK RISK) |
| Extract Type from Generic | **off** | the methods aren't generic, so the event is the receiver (`Health`) rather than a type argument |
| ScriptableObject Bus | off | signals are fields, not assets |
| Chip Label, Node Color, Node Shape | as you like | |

3. Scan. The card is saved in `ProjectSettings/LookoutConfig.json` under `AdditionalBuses` and is shared with your
   team through version control.

**What it finds**, for example in this code:

- `Health` is raised by `SignalsPlayer.TakeHit` (via `SetValue`) and heard by `SignalsHud.OnHealth`, deregistered.
- `Target` is raised by `SignalsPlayer.Aim` and heard by `SignalsHud.OnTarget`.
- `IsDead` is a computed signal: heard by `SignalsHud.OnDeadChanged`, never raised by your code (the library
  recomputes it when `Health` changes).

**What it misses:**

- `Health.Value += 5` is a property set, not a method call, so it isn't a raise.
- `transaction.Set(Health, 100)` is a different method on a different receiver.
- `Mana.SignalValueChanged += OnManaChanged` appears as a C# event node `SignalValueChanged`, not as a listener of
  `Mana`.

If these matter, use Way B.

## 4. Way B: a dedicated scanner

A scanner is an editor class that reads each source file and reports what it finds. Lookout finds it on its own: no
registration, no setting. Implement the interface, and the next scan uses it.

```csharp
namespace VoidCat.Lookout {
    public interface IEventGraphScanner {
        EventsScanResult Scan(string filePath, string fileContent, LookoutConfig config);
    }
}
```

### Step 1: an editor assembly

Create an `Editor` folder in your project with an assembly definition that:

- includes only the **Editor** platform
- references `VoidCat.Lookout.Editor`
- has the define constraint `!LOOKOUT_DISABLED`, so the project still compiles when Lookout is turned off.

The scanner reads source text only, so it doesn't need to reference Unity Signals. The copy in
`Documentation~/Lookout/` also compiles it only when the `unitysignals` package is installed (a version define and a
second constraint, `LOOKOUT_EXT_UNITYSIGNALS`). That is optional.

### Step 2: the rules

- **A public class with a public parameterless constructor.** Lookout creates it through `TypeCache`, so an abstract
  class or one without that constructor is skipped.
- **`Scan` runs on every scanned file.** Leave early for files that don't use your library and return `null`. This
  scanner checks for `DGP.UnitySignals`, the namespace every file that uses the library imports.
- **Return sites, not a graph.** Fill an `EventsScanResult`:
  - `Declarations` (`RawEventDecl`): where an event is declared. It creates the node.
  - `RaiseSites` (`RawRaiseSite`): where it is raised.
  - `SubscribeSites` (`RawSubscribeSite`): where it is listened to. Set `IsUnsubscribe = true` for the call that
    stops it.
  - `DeregisterSites` (`RawDeregisterSite`): for libraries where cleanup is a separate object, such as R3's
    disposables.

  Lookout merges these with its own scan and builds the graph.
- **Match by `EventTypeName`.** A raise or listener joins the node with the same `EventTypeName`. Here that is the
  signal's field name.
- **Set `BusName` on every site and declaration.** It makes the nodes purple custom-bus nodes with a filter chip of
  their own. Settings ▸ Display then lists the bus with a color and shape you can change.
- **`RequiresDeclaration = true`** on a raise means: only if a declaration of that name exists. Use it for broad
  patterns. `.Value =` is common, and this keeps a slider or a NetworkVariable from becoming a signal.
- **Set `EnclosingClass` and `EnclosingMethod`.** They become the node's publisher and listener rows, and the
  method a listener stops in (shown in Details as its deregister).
- **Never throw.** An exception skips your scanner for that file and logs a warning; the rest of the scan
  continues.

### Step 3: the scanner

This is a complete scanner for the patterns in section 2. Copy it, with its assembly definition, from
`Documentation~/Lookout/` into your project's `Editor` folder (Unity doesn't compile folders ending in `~`).

```csharp
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
```

### Step 4: replace the custom bus

If you also added the custom bus from Way A, remove its card. Otherwise the two report the same `SetValue` and
`AddObserver` calls, and each appears twice.

Scan. For the example code below, the scanner finds:

| Node | Raised by | Heard by |
|---|---|---|
| `Health` | `SignalsPlayer.TakeHit` (SetValue), `.Heal` (`Value +=`), `.Rest` (transaction) | `SignalsHud.OnHealth`, deregistered |
| `Mana` | `SignalsPlayer.Rest` (transaction) | none (see below) |
| `Target` | `SignalsPlayer.Aim` | `SignalsHud.OnTarget`, deregistered |
| `IsDead` | none (computed) | `SignalsHud.OnDeadChanged`, deregistered |

### Taking it further

- **`SignalValueChanged`.** The example HUD listens to `Mana` through the signal's C# event, which Lookout's own scan
  shows as a `SignalValueChanged` node. To show it as a `Mana` listener instead, match
  `(\w+)\.SignalValueChanged\s*([+-])=\s*(\w+)` and add a subscribe site for group 1, with `IsUnsubscribe` set for
  `-=`. The C# event node stays too; filter it out if it gets in the way.
- **Computed signals.** A `ComputedSignal` has no raise site in your code. If you want to see what it depends on,
  read the signals named inside its lambda (`new ComputedSignal<bool>(() => Health.Value <= 0)`) and add a raise
  site for the computed signal in each class that raises one of them.
- **More examples.** Lookout's own scanners use the same interface: `Lookout/Editor/Extensions/R3Scanner.cs` (R3 and
  UniRx, including disposables as cleanup) and `MessagePipeScanner.cs`, in the CodeCompass package.

## 5. Live pulses and the Trace

The custom bus and the scanner only build the graph. Nothing in UnitySignals reports a change globally, and Lookout
doesn't weave the library. So a signal pulses only if your code reports it.

### With Lookout's attributes

Mark the raising method and the listener:

```csharp
public sealed class SignalsMarkedPlayer : MonoBehaviour {
    [LookoutEvent("Player/Health")]
    public readonly IntegerValueSignal Health = new IntegerValueSignal(100);

    [LookoutRaises("Player/Health")]
    public void TakeHit(int amount) => Health.Value -= amount;
}

public sealed class SignalsMarkedHud : MonoBehaviour {
    public SignalsMarkedPlayer Player;
    void OnEnable()  => Player.Health.AddObserver(OnHealth);
    void OnDisable() => Player.Health.RemoveObserver(OnHealth);

    [LookoutListens("Player/Health")]
    void OnHealth(int value) => Debug.Log(value);
}
```

The assembly with the marked code must reference **`VoidCat.Lookout.Attributes`** (for the attributes) and
**`VoidCat.Lookout.Editor`**. Lookout weaves a marked method only in an assembly that references it. Without that
reference, the marks still appear on the graph, but nothing pulses.

Each call to a `[LookoutRaises]` method then pulses `Player/Health` and is added to the Trace.

A mark names its own event (`Player/Health`), separate from the `Health` node that Way A or B creates. Use one name
per signal: either mark it, or scan it and report it as below.

### From a change handler (not yet tried)

Each signal has its own `SignalValueChanged` event. A component can forward it to Lookout under the node's name:

```csharp
#if UNITY_EDITOR
void OnEnable()  => Health.SignalValueChanged += ReportHealth;
void OnDisable() => Health.SignalValueChanged -= ReportHealth;
void ReportHealth(IEmitSignals<int> signal, int oldValue, int newValue) =>
    VoidCat.Lookout.LookoutProbe.RecordRaiseByName("Health", newValue);
#endif
```

The name must match the node's (`Health` with Way A or B). The assembly must reference `VoidCat.Lookout.Editor`, and
the `#if UNITY_EDITOR` keeps the call out of player builds. The probe does nothing unless Play is on and a Lookout
window is open. The pulse starts at the node rather than at the method that changed the value, and the forwarding
handler itself appears on the graph as a `SignalValueChanged` listener.

## The example code

The tables above come from these two components:

```csharp
using DGP.UnitySignals;
using DGP.UnitySignals.Signals;
using UnityEngine;

namespace MyGame {

    /// <summary>owns the signals: a value signal set by method, by property and in a
    /// transaction; a computed signal that depends on it; a reference signal.</summary>
    public sealed class SignalsPlayer : MonoBehaviour {
        public readonly IntegerValueSignal Health = new IntegerValueSignal(100);
        public readonly IntegerValueSignal Mana   = new IntegerValueSignal(50);
        public readonly ReferenceSignal<Transform> Target = new ReferenceSignal<Transform>();
        public ComputedSignal<bool> IsDead { get; private set; }

        void Awake() => IsDead = new ComputedSignal<bool>(() => Health.Value <= 0);

        public void TakeHit(int amount) => Health.SetValue(Health.Value - amount);   // set by method
        public void Heal(int amount)    => Health.Value += amount;                     // set by property
        public void Aim(Transform t)    => Target.SetValue(t);

        /// <summary>Two changes, one notification each, sent together on commit.</summary>
        public void Rest() {
            using var transaction = new SignalTransaction();
            transaction.Set(Health, 100).Set(Mana, 50);
            transaction.Commit();
        }
    }
}
```

```csharp
using DGP.UnitySignals;
using UnityEngine;

namespace MyGame {

    /// <summary>observes them three ways: <c>AddObserver</c> with a value handler, the same on the
    /// computed and reference signals, and the <c>SignalValueChanged</c> event.</summary>
    public sealed class SignalsHud : MonoBehaviour {
        public SignalsPlayer Player;

        void OnEnable() {
            Player.Health.AddObserver(OnHealth);
            Player.IsDead.AddObserver(OnDeadChanged);
            Player.Target.AddObserver(OnTarget);
            Player.Mana.SignalValueChanged += OnManaChanged;
        }

        void OnDisable() {
            Player.Health.RemoveObserver(OnHealth);
            Player.IsDead.RemoveObserver(OnDeadChanged);
            Player.Target.RemoveObserver(OnTarget);
            Player.Mana.SignalValueChanged -= OnManaChanged;
        }

        void OnHealth(int value)        => Debug.Log($"[UnitySignals] health {value}");
        void OnDeadChanged(bool dead)   => Debug.Log($"[UnitySignals] dead: {dead}");
        void OnTarget(Transform target) => Debug.Log($"[UnitySignals] target {(target ? target.name : "none")}");
        void OnManaChanged(IEmitSignals<int> sender, int oldValue, int newValue) =>
            Debug.Log($"[UnitySignals] mana {oldValue} → {newValue}");
    }
}
```
