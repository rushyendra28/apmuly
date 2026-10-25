using Apmuly.Core.Text;

namespace Apmuly.Core.Insights;

/// <summary>
/// Emoji for emotions, actions, and concrete things commonly mentioned in songs.
/// Only Unicode 13.0 or earlier and no joined (ZWJ) sequences, matching the Mac app.
/// </summary>
public static class EmojiLexicon
{
    private static readonly Dictionary<Emotion, string[]> Emotions = new()
    {
        [Emotion.Joy] = ["😄", "😁", "🤩", "😊", "🌞"],
        [Emotion.Love] = ["😍", "🥰", "💕", "❤️", "💘"],
        [Emotion.Sadness] = ["😢", "😔", "🥺", "😞", "🌧️"],
        [Emotion.Anger] = ["😠", "😤", "💢", "🤬", "🔥"],
        [Emotion.Excitement] = ["🥳", "🎉", "🤘", "🔥", "⚡"],
        [Emotion.Calm] = ["😌", "🌿", "☁️", "🍃", "🌊"],
        [Emotion.Fear] = ["😨", "😱", "👀", "😰", "🙈"],
        [Emotion.Confidence] = ["😎", "💪", "👑", "💯", "🏆"],
        [Emotion.Heartbreak] = ["💔", "😞", "🥀", "😿", "🖤"],
        [Emotion.Longing] = ["🥺", "💭", "💞", "🌙", "⏳"],
    };

    /// <summary>Verbs and their common forms.</summary>
    private static readonly Dictionary<string, string> Actions = new()
    {
        { "dance", "💃" }, { "dancing", "💃" }, { "danced", "💃" }, { "run", "🏃" }, { "running", "🏃" }, { "ran", "🏃" },
        { "fly", "🕊️" }, { "flying", "🕊️" }, { "flew", "🕊️" }, { "drive", "🏎️" }, { "driving", "🏎️" }, { "drove", "🏎️" },
        { "sing", "🎤" }, { "singing", "🎤" }, { "sang", "🎤" }, { "cry", "😭" }, { "crying", "😭" }, { "cried", "😭" },
        { "laugh", "😂" }, { "laughing", "😂" }, { "smile", "😊" }, { "smiling", "😊" }, { "sleep", "😴" }, { "sleeping", "😴" },
        { "swim", "🏊" }, { "swimming", "🏊" }, { "jump", "🤸" }, { "jumping", "🤸" }, { "call", "📞" }, { "calling", "📞" },
        { "pray", "🙏" }, { "praying", "🙏" }, { "fight", "🥊" }, { "fighting", "🥊" }, { "hug", "🤗" }, { "kiss", "💋" },
        { "kissing", "💋" }, { "shine", "✨" }, { "shining", "✨" }, { "burn", "🔥" }, { "burning", "🔥" }, { "fall", "🍂" },
        { "falling", "🍂" }, { "wait", "⏳" }, { "waiting", "⏳" }, { "think", "🤔" }, { "thinking", "🤔" }, { "scream", "😱" },
        { "screaming", "😱" }, { "whisper", "🤫" }, { "cook", "🍳" }, { "write", "✍️" }, { "writing", "✍️" }, { "ride", "🏇" },
        { "riding", "🏇" }, { "climb", "🧗" }, { "celebrate", "🥳" }, { "walk", "🚶" }, { "walking", "🚶" }, { "shop", "🛍️" },
        { "pay", "💳" }, { "travel", "🧳" }, { "wink", "😉" }, { "sweat", "😅" }, { "kneel", "🧎" }, { "clap", "👏" },

    };

    private static readonly Dictionary<string, string> Objects = new()
    {
        // Sky & weather
        { "moon", "🌙" }, { "sun", "☀️" }, { "star", "⭐" }, { "sky", "🌌" }, { "cloud", "☁️" }, { "rain", "🌧️" }, { "storm", "⛈️" },
        { "thunder", "⚡" }, { "lightning", "⚡" }, { "snow", "❄️" }, { "rainbow", "🌈" }, { "wind", "🌬️" }, { "sunset", "🌅" },
        { "sunrise", "🌄" }, { "night", "🌃" }, { "planet", "🪐" }, { "galaxy", "🌌" }, { "comet", "☄️" }, { "tornado", "🌪️" },
        { "fog", "🌫️" }, { "heat", "🥵" }, { "ice", "🧊" }, { "frost", "🥶" },
        // Nature
        { "ocean", "🌊" }, { "sea", "🌊" }, { "wave", "🌊" }, { "river", "🏞️" }, { "lake", "🏞️" }, { "beach", "🏖️" },
        { "mountain", "⛰️" }, { "fire", "🔥" }, { "flame", "🔥" }, { "tree", "🌳" }, { "forest", "🌲" }, { "flower", "🌸" },
        { "rose", "🌹" }, { "sunflower", "🌻" }, { "tulip", "🌷" }, { "leaf", "🍃" }, { "island", "🏝️" }, { "desert", "🏜️" },
        { "volcano", "🌋" }, { "earth", "🌍" }, { "world", "🌍" }, { "cactus", "🌵" }, { "mushroom", "🍄" }, { "palm", "🌴" },
        { "rock", "🪨" }, { "stone", "🪨" }, { "sand", "🏖️" }, { "shell", "🐚" }, { "feather", "🪶" }, { "garden", "🌷" },
        { "grass", "🌱" }, { "seed", "🌱" }, { "petal", "🌸" }, { "wood", "🪵" },
        // Animals
        { "bird", "🐦" }, { "butterfly", "🦋" }, { "dog", "🐶" }, { "puppy", "🐶" }, { "cat", "🐱" }, { "kitten", "🐱" },
        { "lion", "🦁" }, { "wolf", "🐺" }, { "snake", "🐍" }, { "horse", "🐴" }, { "fish", "🐟" }, { "shark", "🦈" },
        { "tiger", "🐯" }, { "bee", "🐝" }, { "bear", "🐻" }, { "monkey", "🐒" }, { "owl", "🦉" }, { "eagle", "🦅" },
        { "dove", "🕊️" }, { "crow", "🐦" }, { "raven", "🐦" }, { "spider", "🕷️" }, { "dragon", "🐉" }, { "unicorn", "🦄" },
        { "rabbit", "🐰" }, { "bunny", "🐰" }, { "fox", "🦊" }, { "deer", "🦌" }, { "whale", "🐳" }, { "dolphin", "🐬" },
        { "octopus", "🐙" }, { "frog", "🐸" }, { "pig", "🐷" }, { "cow", "🐮" }, { "chicken", "🐔" }, { "duck", "🦆" },
        { "goat", "🐐" }, { "sheep", "🐑" }, { "mouse", "🐭" }, { "rat", "🐀" }, { "bat", "🦇" }, { "turtle", "🐢" },
        { "penguin", "🐧" }, { "elephant", "🐘" }, { "zebra", "🦓" }, { "giraffe", "🦒" }, { "panda", "🐼" }, { "parrot", "🦜" },
        { "flamingo", "🦩" }, { "peacock", "🦚" }, { "swan", "🦢" }, { "jellyfish", "🐙" }, { "crab", "🦀" }, { "scorpion", "🦂" },
        { "dinosaur", "🦖" }, { "phoenix", "🔥" },
        // People & body
        { "heart", "❤️" }, { "eye", "👁️" }, { "hand", "✋" }, { "lip", "👄" }, { "mouth", "👄" }, { "nose", "👃" }, { "ear", "👂" },
        { "tongue", "👅" }, { "brain", "🧠" }, { "bone", "🦴" }, { "blood", "🩸" }, { "fist", "✊" }, { "finger", "☝️" },
        { "foot", "🦶" }, { "leg", "🦵" }, { "arm", "💪" }, { "muscle", "💪" }, { "crown", "👑" }, { "angel", "😇" }, { "devil", "😈" },
        { "ghost", "👻" }, { "skull", "💀" }, { "king", "🤴" }, { "queen", "👸" }, { "prince", "🤴" }, { "princess", "👸" },
        { "baby", "👶" }, { "tear", "💧" }, { "teardrop", "💧" }, { "zombie", "🧟" }, { "vampire", "🧛" }, { "witch", "🧙" },
        { "alien", "👽" }, { "robot", "🤖" }, { "clown", "🤡" }, { "ninja", "🥷" }, { "pirate", "☠️" }, { "cowboy", "🤠" },
        { "astronaut", "🚀" }, { "superhero", "🦸" }, { "mermaid", "🧜" }, { "fairy", "🧚" },
        // Vehicles & places
        { "car", "🚗" }, { "plane", "✈️" }, { "jet", "✈️" }, { "train", "🚆" }, { "boat", "⛵" }, { "ship", "🚢" }, { "bike", "🚲" },
        { "rocket", "🚀" }, { "truck", "🚚" }, { "bus", "🚌" }, { "taxi", "🚕" }, { "motorcycle", "🏍️" }, { "helicopter", "🚁" },
        { "subway", "🚇" }, { "spaceship", "🛸" }, { "ufo", "🛸" }, { "road", "🛣️" }, { "highway", "🛣️" }, { "street", "🛣️" },
        { "bridge", "🌉" }, { "city", "🏙️" }, { "town", "🏘️" }, { "house", "🏠" }, { "home", "🏡" }, { "door", "🚪" },
        { "window", "🪟" }, { "bed", "🛏️" }, { "room", "🛋️" }, { "couch", "🛋️" }, { "kitchen", "🍳" }, { "school", "🏫" },
        { "church", "⛪" }, { "hospital", "🏥" }, { "club", "🕺" }, { "bar", "🍸" }, { "hotel", "🏨" }, { "castle", "🏰" },
        { "park", "🏞️" }, { "airport", "🛫" }, { "pool", "🏊" }, { "stage", "🎭" }, { "studio", "🎙️" }, { "tower", "🗼" },
        { "prison", "⛓️" }, { "jail", "⛓️" }, { "farm", "🚜" }, { "tent", "⛺" }, { "factory", "🏭" }, { "office", "🏢" },
        { "bank", "🏦" }, { "stadium", "🏟️" }, { "temple", "🛕" }, { "palace", "🏰" }, { "skyscraper", "🏙️" },
        // Food & drink
        { "bottle", "🍾" }, { "wine", "🍷" }, { "champagne", "🍾" }, { "beer", "🍺" }, { "drink", "🍹" }, { "cocktail", "🍸" },
        { "coffee", "☕" }, { "tea", "🍵" }, { "milk", "🥛" }, { "water", "💧" }, { "cake", "🎂" }, { "candy", "🍬" },
        { "cherry", "🍒" }, { "apple", "🍎" }, { "pizza", "🍕" }, { "burger", "🍔" }, { "fries", "🍟" }, { "chocolate", "🍫" },
        { "strawberry", "🍓" }, { "lemon", "🍋" }, { "peach", "🍑" }, { "banana", "🍌" }, { "grape", "🍇" }, { "bread", "🍞" },
        { "honey", "🍯" }, { "cookie", "🍪" }, { "donut", "🍩" }, { "popcorn", "🍿" }, { "cheese", "🧀" }, { "egg", "🥚" },
        { "taco", "🌮" }, { "sushi", "🍣" }, { "noodle", "🍜" }, { "rice", "🍚" }, { "pie", "🥧" }, { "lollipop", "🍭" },
        { "watermelon", "🍉" }, { "pineapple", "🍍" }, { "coconut", "🥥" }, { "mango", "🥭" }, { "avocado", "🥑" },
        { "pepper", "🌶️" }, { "salt", "🧂" }, { "sugar", "🍬" }, { "icecream", "🍦" }, { "juice", "🧃" }, { "whiskey", "🥃" },
        { "tequila", "🥃" }, { "vodka", "🥃" },
        // Clothes & style
        { "glasses", "👓" }, { "sunglasses", "🕶️" }, { "shoe", "👟" }, { "sneaker", "👟" }, { "boot", "👢" }, { "heel", "👠" },
        { "dress", "👗" }, { "hat", "🎩" }, { "cap", "🧢" }, { "jacket", "🧥" }, { "coat", "🧥" }, { "jeans", "👖" }, { "shirt", "👕" },
        { "lipstick", "💄" }, { "makeup", "💄" }, { "perfume", "🌸" }, { "necklace", "📿" }, { "bikini", "👙" }, { "tie", "👔" },
        { "scarf", "🧣" }, { "glove", "🧤" }, { "sock", "🧦" }, { "purse", "👛" }, { "bag", "👜" }, { "backpack", "🎒" },
        { "suitcase", "🧳" }, { "umbrella", "☂️" },
        // Things & tech
        { "phone", "📱" }, { "money", "💵" }, { "cash", "💸" }, { "dollar", "💵" }, { "coin", "🪙" }, { "wallet", "👛" },
        { "diamond", "💎" }, { "gold", "🥇" }, { "silver", "🥈" }, { "ring", "💍" }, { "key", "🔑" }, { "clock", "⏰" },
        { "watch", "⌚" }, { "gun", "🔫" }, { "knife", "🔪" }, { "sword", "🗡️" }, { "shield", "🛡️" }, { "bow", "🏹" },
        { "arrow", "🏹" }, { "bomb", "💣" }, { "guitar", "🎸" }, { "piano", "🎹" }, { "drum", "🥁" }, { "violin", "🎻" },
        { "trumpet", "🎺" }, { "saxophone", "🎷" }, { "microphone", "🎤" }, { "mic", "🎤" }, { "radio", "📻" }, { "music", "🎵" },
        { "song", "🎶" }, { "record", "💿" }, { "vinyl", "💿" }, { "camera", "📷" }, { "video", "📹" }, { "movie", "🎬" },
        { "film", "🎞️" }, { "tv", "📺" }, { "television", "📺" }, { "computer", "💻" }, { "laptop", "💻" },
        { "headphones", "🎧" }, { "speaker", "🔊" }, { "light", "💡" }, { "lamp", "💡" }, { "battery", "🔋" },
        { "satellite", "🛰️" }, { "controller", "🎮" }, { "letter", "✉️" }, { "envelope", "✉️" }, { "book", "📖" },
        { "paper", "📄" }, { "pen", "🖊️" }, { "pencil", "✏️" }, { "paint", "🎨" }, { "brush", "🖌️" }, { "scissors", "✂️" },
        { "hammer", "🔨" }, { "rope", "🪢" }, { "ladder", "🪜" }, { "bell", "🔔" }, { "anchor", "⚓" }, { "kite", "🪁" },
        { "puzzle", "🧩" }, { "dice", "🎲" }, { "card", "🃏" }, { "gift", "🎁" }, { "present", "🎁" }, { "balloon", "🎈" },
        { "candle", "🕯️" }, { "mirror", "🪞" }, { "chain", "⛓️" }, { "lock", "🔒" }, { "magic", "✨" }, { "party", "🎉" },
        { "ticket", "🎟️" }, { "map", "🗺️" }, { "compass", "🧭" }, { "smoke", "💨" }, { "flag", "🚩" }, { "trophy", "🏆" },
        { "medal", "🏅" }, { "pill", "💊" }, { "medicine", "💊" }, { "cigarette", "🚬" }, { "telescope", "🔭" },
        { "crystal", "🔮" }, { "magnet", "🧲" }, { "candlelight", "🕯️" }, { "firework", "🎆" }, { "fireworks", "🎆" },
        { "confetti", "🎊" }, { "disco", "🕺" }, { "spotlight", "🔦" }, { "flashlight", "🔦" }, { "tattoo", "🖋️" },
        { "pillow", "🛏️" }, { "blanket", "🛌" }, { "chair", "🪑" }, { "table", "🍽️" }, { "glass", "🥂" }, { "cup", "☕" },
        { "plate", "🍽️" }, { "box", "📦" }, { "photo", "🖼️" }, { "picture", "🖼️" }, { "painting", "🖼️" }, { "statue", "🗽" },

    };

    private static readonly HashSet<string> Abstract =
    [
        "time", "life", "way", "thing", "feeling", "mind", "soul", "truth", "lie", "memory", "reason",
        "chance", "nothing", "everything", "something", "anything", "moment", "place", "end", "beginning",
        "love", "hate", "pain", "fear", "hope", "day", "tonight", "today", "tomorrow", "yesterday",
        "dream", "word", "story", "game", "lesson", "side", "part", "kind", "lot",

    ];

    public static int ObjectCount => Objects.Count;

    public static int ActionCount => Actions.Count;

    /// <summary>Every emoji Apmuly can show (for compatibility checks).</summary>
    internal static IReadOnlyList<string> AllEmoji =>
        Emotions.Values.SelectMany(e => e).Concat(Actions.Values).Concat(Objects.Values).Distinct().Order(StringComparer.Ordinal).ToArray();

    public static string? EmojiFor(Emotion emotion, int variant)
    {
        if (!Emotions.TryGetValue(emotion, out var options) || options.Length == 0)
        {
            return null;
        }
        return options[(int)(Math.Abs((long)variant) % options.Length)];
    }

    public static string? EmojiForObject(string word)
    {
        var key = Normalize(word);
        if (key.Length == 0 || IsAbstract(key))
        {
            return null;
        }
        return Candidates(key).Select(form => Objects.GetValueOrDefault(form)).FirstOrDefault(e => e is not null);
    }

    /// <summary>Emoji for an action word ("dancing" → 💃). Exact forms only.</summary>
    public static string? EmojiForAction(string word) => Actions.GetValueOrDefault(Normalize(word));

    public static bool IsAbstract(string word) => Candidates(Normalize(word)).Any(Abstract.Contains);

    /// <summary>Lowercased, letters only, singular where a lexicon entry exists.</summary>
    public static string Canonical(string word)
    {
        var key = Normalize(word);
        return Candidates(key).FirstOrDefault(form => Objects.ContainsKey(form) || Abstract.Contains(form)) ?? Singular(key);
    }

    /// <summary>Lowercased; letters, spaces and hyphens only; trimmed.</summary>
    internal static string Normalize(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return TextFolding.Filter(word.ToLowerInvariant(), r => System.Text.Rune.IsLetter(r) || r.Value == ' ' || r.Value == '-').Trim();
    }

    /// <summary>The word itself, then simple singular forms (cities → city, boxes → box, roses → rose).</summary>
    internal static IReadOnlyList<string> Candidates(string key)
    {
        var forms = new List<string> { key };
        var length = TextFolding.RuneCount(key);
        if (key.EndsWith("ies", StringComparison.Ordinal) && length > 4)
        {
            forms.Add(key[..^3] + "y");
        }
        if (key.EndsWith("es", StringComparison.Ordinal) && length > 3)
        {
            forms.Add(key[..^2]);
        }
        if (key.EndsWith('s') && !key.EndsWith("ss", StringComparison.Ordinal) && length > 2)
        {
            forms.Add(key[..^1]);
        }
        return forms;
    }

    private static string Singular(string key)
    {
        var length = TextFolding.RuneCount(key);
        if (key.EndsWith("ies", StringComparison.Ordinal) && length > 4)
        {
            return key[..^3] + "y";
        }
        if (key.EndsWith('s') && !key.EndsWith("ss", StringComparison.Ordinal) && length > 3)
        {
            return key[..^1];
        }
        return key;
    }
}
