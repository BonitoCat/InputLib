using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace InputLib;

public sealed class MouseButton
{
    private static readonly List<MouseButton> Buttons = [];
    private static readonly Dictionary<uint, MouseButton> _byId = new();

    public static readonly MouseButton None = new(0, "None");
    public static readonly MouseButton Left = new(1, "Left");
    public static readonly MouseButton Middle = new(2, "Middle");
    public static readonly MouseButton Right = new(3, "Right");
    public static readonly MouseButton ScrollUp = new(4, "ScrollUp");
    public static readonly MouseButton ScrollDown = new(5, "ScrollDown");
    public static readonly MouseButton ScrollLeft = new(6, "ScrollLeft");
    public static readonly MouseButton ScrollRight = new(7, "ScrollRight");
    public static readonly MouseButton Back = new(8, "Back");
    public static readonly MouseButton Forward = new(9, "Forward");

    public uint Id { get; }
    public string Name { get; }

    static MouseButton()
    {
        foreach (FieldInfo field in typeof(MouseButton)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(MouseButton)))
        {
            MouseButton btn = (MouseButton) field.GetValue(null)!;
            _byId[btn.Id] = btn;
        }
    }

    private MouseButton(uint id, string name)
    {
        Id = id;
        Name = name;
        Buttons.Add(this);
    }

    public static MouseButton FromId(uint id)
    {
        if (_byId.TryGetValue(id, out MouseButton? button))
        {
            return button;
        }

        return new MouseButton(id, "Button" + id.ToString());
    }

    public static bool TryParse(string name, out MouseButton? button)
    {
        button = Buttons.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
        return button is not null;
    }

    public override string ToString() => Name;

    public override bool Equals(object? obj) => obj is MouseButton b && Id == b.Id;

    public override int GetHashCode() => (int) Id;

    public static bool operator ==(MouseButton? a, MouseButton? b) => a?.Id == b?.Id;

    public static bool operator !=(MouseButton? a, MouseButton? b) => !(a == b);

    public static implicit operator MouseButton(uint id) => FromId(id);

    public static implicit operator uint(MouseButton button) => button.Id;
}
