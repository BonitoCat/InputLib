namespace InputLib;

public sealed class Key
{
    private static List<Key> Keys = [];
    
    public static readonly Key None = new(0, "None");

    public static readonly Key Escape = new(1, "Escape");
    public static readonly Key LeftShift = new(2, "LeftShift");
    public static readonly Key RightShift = new(3, "RightShift");
    public static readonly Key Shift = new(201, "Shift");
    public static readonly Key Ctrl = new(202, "Ctrl");
    public static readonly Key CapsLock = new(4, "CapsLock");
    public static readonly Key LeftCtrl = new(5, "LeftCtrl");
    public static readonly Key RightCtrl = new(6, "RightCtrl");
    public static readonly Key Alt = new(7, "Alt");
    public static readonly Key AltGr = new(8, "AltGr");
    public static readonly Key Tab = new(9, "Tab");
    public static readonly Key Win = new(10, "Win");
    public static readonly Key Menu = new(11, "Menu");
    public static readonly Key Enter = new(12, "Enter");
    public static readonly Key Space = new(13, "Space");
    public static readonly Key Backspace = new(14, "Backspace");
    public static readonly Key Print = new(15, "Print");
    public static readonly Key Insert = new(16, "Insert");
    public static readonly Key Delete = new(17, "Delete");

    public static readonly Key F1 = new(18, "F1");
    public static readonly Key F2 = new(19, "F2");
    public static readonly Key F3 = new(20, "F3");
    public static readonly Key F4 = new(21, "F4");
    public static readonly Key F5 = new(22, "F5");
    public static readonly Key F6 = new(23, "F6");
    public static readonly Key F7 = new(24, "F7");
    public static readonly Key F8 = new(25, "F8");
    public static readonly Key F9 = new(26, "F9");
    public static readonly Key F10 = new(27, "F10");
    public static readonly Key F11 = new(28, "F11");
    public static readonly Key F12 = new(29, "F12");
    public static readonly Key F13 = new(30, "F13");
    public static readonly Key F14 = new(31, "F14");
    public static readonly Key F15 = new(32, "F15");
    public static readonly Key F16 = new(33, "F16");
    public static readonly Key F17 = new(34, "F17");
    public static readonly Key F18 = new(35, "F18");
    public static readonly Key F19 = new(36, "F19");
    public static readonly Key F20 = new(37, "F20");
    public static readonly Key F21 = new(38, "F21");
    public static readonly Key F22 = new(39, "F22");
    public static readonly Key F23 = new(40, "F23");
    public static readonly Key F24 = new(41, "F24");

    public static readonly Key D0 = new(42, "D0", '0');
    public static readonly Key D1 = new(43, "D1", '1');
    public static readonly Key D2 = new(44, "D2", '2');
    public static readonly Key D3 = new(45, "D3", '3');
    public static readonly Key D4 = new(46, "D4", '4');
    public static readonly Key D5 = new(47, "D5", '5');
    public static readonly Key D6 = new(48, "D6", '6');
    public static readonly Key D7 = new(49, "D7", '7');
    public static readonly Key D8 = new(50, "D8", '8');
    public static readonly Key D9 = new(51, "D9", '9');

    public static readonly Key NumLock = new(52, "NumLock");
    public static readonly Key N0 = new(53, "N0", '0');
    public static readonly Key N1 = new(54, "N1", '1');
    public static readonly Key N2 = new(55, "N2", '2');
    public static readonly Key N3 = new(56, "N3", '3');
    public static readonly Key N4 = new(57, "N4", '4');
    public static readonly Key N5 = new(58, "N5", '5');
    public static readonly Key N6 = new(59, "N6", '6');
    public static readonly Key N7 = new(60, "N7", '7');
    public static readonly Key N8 = new(61, "N8", '8');
    public static readonly Key N9 = new(62, "N9", '9');

    public static readonly Key Backtick = new(63, "Backtick", '`');
    public static readonly Key Minus = new(64, "Minus", '-');
    public static readonly Key Plus = new(65, "Plus", '+');
    public static readonly Key Equal = new(66, "Equals", '=');
    public static readonly Key Home = new(67, "Home");
    public static readonly Key End = new(68, "End");
    public static readonly Key PageUp = new(69, "PageUp");
    public static readonly Key PageDown = new(70, "PageDown");

    public static readonly Key A = new(71, "A", 'a');
    public static readonly Key B = new(72, "B", 'b');
    public static readonly Key C = new(73, "C", 'c');
    public static readonly Key D = new(74, "D", 'd');
    public static readonly Key E = new(75, "E", 'e');
    public static readonly Key F = new(76, "F", 'f');
    public static readonly Key G = new(77, "G", 'g');
    public static readonly Key H = new(78, "H", 'h');
    public static readonly Key I = new(79, "I", 'i');
    public static readonly Key J = new(80, "J", 'j');
    public static readonly Key K = new(81, "K", 'k');
    public static readonly Key L = new(82, "L", 'l');
    public static readonly Key M = new(83, "M", 'm');
    public static readonly Key N = new(84, "N", 'n');
    public static readonly Key O = new(85, "O", 'o');
    public static readonly Key P = new(86, "P", 'p');
    public static readonly Key Q = new(87, "Q", 'q');
    public static readonly Key R = new(88, "R", 'r');
    public static readonly Key S = new(89, "S", 's');
    public static readonly Key T = new(90, "T", 't');
    public static readonly Key U = new(91, "U", 'u');
    public static readonly Key V = new(92, "V", 'v');
    public static readonly Key W = new(93, "W", 'w');
    public static readonly Key X = new(94, "X", 'x');
    public static readonly Key Y = new(95, "Y", 'y');
    public static readonly Key Z = new(96, "Z", 'z');

    public static readonly Key Comma = new(97, "Comma", ',');
    public static readonly Key Period = new(98, "Period", '.');
    public static readonly Key Slash = new(99, "Slash", '/');
    public static readonly Key BackSlash = new(100, "BackSlash", '\\');
    public static readonly Key SemiColon = new(101, "SemiColon", ';');
    public static readonly Key Apostrophe = new(102, "Apostrophe", '\'');
    public static readonly Key SquareBracketLeft = new(103, "SquareBracketLeft", '[');
    public static readonly Key SquareBracketRight = new(104, "SquareBracketRight", ']');

    public static readonly Key ArrowUp = new(105, "ArrowUp");
    public static readonly Key ArrowDown = new(106, "ArrowDown");
    public static readonly Key ArrowLeft = new(107, "ArrowLeft");
    public static readonly Key ArrowRight = new(108, "ArrowRight");

    private static readonly Dictionary<ushort, Key> _byId;
    private static readonly Dictionary<char, Key> _byChar;

    public ushort Id { get; }
    public string Name { get; }
    public char Char { get; }

    static Key()
    {
        _byId = new Dictionary<ushort, Key>();
        _byChar = new Dictionary<char, Key>();

        foreach (System.Reflection.FieldInfo field in typeof(Key)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Key)))
        {
            Key k = (Key)field.GetValue(null)!;
            _byId[k.Id] = k;
            
            if (k.Char != '\0')
            {
                _byChar[k.Char] = k;
            }
        }
    }

    private Key(ushort id, string name, char c = '\0')
    {
        Id = id;
        Name = name;
        Char = c;
        
        Keys.Add(this);
    }

    public static Key? FromId(ushort id) => _byId.TryGetValue(id, out Key? k) ? k : null;
    public static Key? FromChar(char c) => _byChar.TryGetValue(c, out Key? k) ? k : null;

    public static bool TryParse(string name, out Key? key)
    {
        key = Keys.FirstOrDefault(key => key.Name == name);
        return key is not null;
    }

    public override string ToString() => Name;
    public override bool Equals(object? obj) => obj is Key k && Id == k.Id;
    public override int GetHashCode() => Id;
    public static bool operator ==(Key? a, Key? b) => a?.Id == b?.Id;
    public static bool operator !=(Key? a, Key? b) => !(a == b);
}
