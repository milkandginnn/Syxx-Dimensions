# Desktop Accessibility — Detailed Reference

## SwiftUI Accessibility Patterns

### Annotated Navigation

```swift
NavigationSplitView {
    List(selection: $selectedItem) {
        ForEach(items) { item in
            ItemRow(item: item)
                .accessibilityLabel("\(item.name), \(item.status)")
                .accessibilityHint("Select to view details")
        }
    }
    .accessibilityLabel("Navigation sidebar")
} detail: {
    DetailView(item: selectedItem)
}
```

### Dynamic Type Support

```swift
Text("Title")
    .font(.system(size: 18, weight: .semibold, design: .default))
    .lineLimit(1)
    .minimumScaleFactor(0.75)
```

### Focus Management

```swift
struct FormView: View {
    @FocusState private var focusedField: Field?
    enum Field { case name, email, password }

    var body: some View {
        Form {
            TextField("Name", text: $name)
                .focused($focusedField, equals: .name)
            TextField("Email", text: $email)
                .focused($focusedField, equals: .email)
        }
        .onAppear { focusedField = .name }
    }
}
```

## WinUI 3 Accessibility Patterns

### AutomationPeer for Custom Controls

```csharp
public class CustomCardAutomationPeer : FrameworkElementAutomationPeer
{
    private readonly CustomCard _owner;

    public CustomCardAutomationPeer(CustomCard owner) : base(owner)
    {
        _owner = owner;
    }

    protected override string GetNameCore()
    {
        return _owner.Title ?? base.GetNameCore();
    }

    protected override string GetHelpTextCore()
    {
        return _owner.Description ?? base.GetHelpTextCore();
    }
}
```

### Keyboard Accelerators

```xml
<Button Content="Save">
    <Button.KeyboardAccelerators>
        <KeyboardAccelerator Key="S" Modifiers="Control"/>
    </Button.KeyboardAccelerators>
</Button>
```

### Live Region Announcements

```csharp
// Programmatic announcement
var peer = FrameworkElementAutomationPeer.FromElement(element)
    ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
peer.RaiseNotificationEvent(
    AutomationNotificationKind.ActionCompleted,
    AutomationNotificationProcessing.ImportantAll,
    "Scan completed successfully",
    "ScanResultNotification");
```

## Avalonia Accessibility Patterns

### Custom AutomationPeer

```csharp
public class CustomCardAutomationPeer : ControlAutomationPeer
{
    private readonly CustomCard _owner;

    public CustomCardAutomationPeer(CustomCard owner) : base(owner)
    {
        _owner = owner;
    }

    protected override string GetNameCore()
    {
        return _owner.Title ?? base.GetNameCore();
    }

    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Group;
    }
}
```

### Focus Navigation

```csharp
// Move focus programmatically
FocusManager.SetFocusedElement(
    TopLevel.GetTopLevel(this),
    targetElement,
    NavigationMethod.Tab,
    KeyModifiers.None);
```

## Qt 6 Accessibility Patterns

### Accessible Widget

```cpp
class AccessibleCard : public QFrame
{
    Q_OBJECT
public:
    AccessibleCard(QWidget* parent = nullptr)
        : QFrame(parent)
    {
        setAccessibleName("Card: " + m_title);
        setAccessibleDescription(m_description);
    }

protected:
    QAccessibleInterface* accessibleInterface() override
    {
        return new CardAccessibleInterface(this);
    }
};
```

### Keyboard Navigation

```cpp
void MyWidget::keyPressEvent(QKeyEvent* event)
{
    switch (event->key()) {
    case Qt::Key_Tab:
        focusNextChild();
        break;
    case Qt::Key_Backtab:
        focusPreviousChild();
        break;
    case Qt::Key_Enter:
    case Qt::Key_Return:
        activateCurrentItem();
        break;
    default:
        QFrame::keyPressEvent(event);
    }
}
```
