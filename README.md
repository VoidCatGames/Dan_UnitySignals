# Unity Signals

A reactive signals library for Unity that provides observable values with automatic change notifications.

## Features

- **Value Signals**: Observable primitive and struct types with equality-based change detection
- **Reference Signals**: Observable reference types with reference equality checking
- **Computed Signals**: Automatically recalculate based on dependencies
- **Transactions**: Batch multiple signal updates with a single notification wave
- **Multiple Observer Types**: Support for delegates, actions, and observer objects
- **Type Safety**: Strongly-typed signal values
- **Memory Management**: Proper disposal and dead dependency tracking

## Basic Usage

### Creating Signals

```csharp
// Value signals for primitives
var health = new IntegerValueSignal(100);
var speed = new FloatValueSignal(5.0f);
var playerName = new StringValueSignal("Player");

// Reference signals for objects
var currentWeapon = new ReferenceSignal<Weapon>();
```

### Observing Changes

```csharp
// Using delegates
health.AddObserver((sender, oldValue, newValue) => {
    Debug.Log($"Health changed from {oldValue} to {newValue}");
});

// Using actions (new value only)
health.AddObserver((newValue) => {
    UpdateHealthBar(newValue);
});

// Using events
health.SignalValueChanged += OnHealthChanged;
```

### Setting Values

```csharp
health.Value = 75;  // Using property
health.SetValue(75); // Using method
```

### Computed Signals

```csharp
var maxHealth = new IntegerValueSignal(100);
var currentHealth = new IntegerValueSignal(80);

var healthPercentage = new ComputedSignal<float>(() => 
    (float)currentHealth.Value / maxHealth.Value
);

// Automatically recalculates when dependencies change
currentHealth.Value = 50;
Debug.Log(healthPercentage.Value); // 0.5
```

## Transactions

Transactions allow you to update multiple signals at once while deferring notifications until all updates are complete. This is useful for maintaining consistency when multiple related signals need to change together.

### Basic Transaction Usage

```csharp
var position = new Vector3ValueSignal(Vector3.zero);
var rotation = new FloatValueSignal(0f);
var scale = new Vector3ValueSignal(Vector3.one);

// All three signals update, but observers only notified once at the end
using (var transaction = new SignalTransaction())
{
    transaction.Set(position, new Vector3(10, 0, 5))
               .Set(rotation, 45f)
               .Set(scale, Vector3.one * 2);
} // Auto-commits on dispose
```

### How Transactions Work

1. **Silent Updates**: Values change immediately but notifications are deferred
2. **Read Current Values**: You can read updated values during the transaction
3. **Single Notification**: Observers are notified once when the transaction commits
4. **Original Old Values**: Observers receive the value from before the transaction started

```csharp
var score = new IntegerValueSignal(0);

score.AddObserver((sender, oldValue, newValue) => {
    Debug.Log($"Score: {oldValue} -> {newValue}");
});

using (var transaction = new SignalTransaction())
{
    transaction.Set(score, 10);
    transaction.Set(score, 20);
    transaction.Set(score, 30);
    
    // Can read current value during transaction
    Debug.Log(score.Value); // Prints: 30
}
// Output: "Score: 0 -> 30" (only one notification, from original to final)
```

### Explicit Commit

```csharp
var transaction = new SignalTransaction();
transaction.Set(signal1, value1);
transaction.Set(signal2, value2);
transaction.Commit(); // Explicit commit
transaction.Dispose(); // Safe to dispose after commit
```

### Transaction Benefits

- **Consistency**: Ensure multiple related signals update atomically
- **Performance**: Reduce notification overhead for batch updates
- **Computed Signal Efficiency**: Computed signals only recalculate once per transaction instead of per dependency change

### Transaction with Computed Signals

```csharp
var firstName = new StringValueSignal("John");
var lastName = new StringValueSignal("Doe");

var fullName = new ComputedSignal<string>(() => 
    $"{firstName.Value} {lastName.Value}"
);

fullName.AddObserver((name) => Debug.Log($"Full name: {name}"));

// Without transaction: two notifications
firstName.Value = "Jane"; // Triggers: "Full name: Jane Doe"
lastName.Value = "Smith";  // Triggers: "Full name: Jane Smith"

// With transaction: one notification
using (var transaction = new SignalTransaction())
{
    transaction.Set(firstName, "Bob")
               .Set(lastName, "Johnson");
} // Triggers once: "Full name: Bob Johnson"
```

## Observer Management

```csharp
// Remove specific observer
void OnHealthChanged(int newValue) { }
health.AddObserver(OnHealthChanged);
health.RemoveObserver(OnHealthChanged);

// Clear all observers
health.ClearObservers();
```

## Disposal

```csharp
// Properly dispose signals when done
health.Dispose();

// Disposed signals mark themselves as dead and clear observers
```

## Available Signal Types

### Basic Value Signals
- `BooleanValueSignal`
- `IntegerValueSignal`
- `FloatValueSignal`
- `DoubleValueSignal`
- `LongValueSignal`
- `ByteValueSignal`
- `DecimalValueSignal`
- `CharValueSignal`
- `StringValueSignal`

### Unity Type Signals
- `Vector2ValueSignal`
- `Vector3ValueSignal`

### Generic Signals
- `ValueSignal<T>` - For any value type
- `ReferenceSignal<T>` - For any reference type
- `ComputedSignal<T>` - For derived values

## Lookout

To see this package's events in Lookout (VoidCat Studios' event-graph tool for Unity), follow
[Documentation~/Lookout.md](Documentation~/Lookout.md). It covers the settings, live pulses and what Lookout can't see
yet; the files it uses are in `Documentation~/Lookout/`.
