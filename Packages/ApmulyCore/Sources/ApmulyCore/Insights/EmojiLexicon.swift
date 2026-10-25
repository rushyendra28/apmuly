import Foundation

/// Emoji for emotions, actions, and concrete things commonly mentioned in songs.
/// Only Unicode 13.0 or earlier and no joined (ZWJ) sequences, so every emoji displays on macOS 12.
public enum EmojiLexicon {
    private static let emotions: [Emotion: [String]] = [
        .joy: ["😄", "😁", "🤩", "😊", "🌞"],
        .love: ["😍", "🥰", "💕", "❤️", "💘"],
        .sadness: ["😢", "😔", "🥺", "😞", "🌧️"],
        .anger: ["😠", "😤", "💢", "🤬", "🔥"],
        .excitement: ["🥳", "🎉", "🤘", "🔥", "⚡"],
        .calm: ["😌", "🌿", "☁️", "🍃", "🌊"],
        .fear: ["😨", "😱", "👀", "😰", "🙈"],
        .confidence: ["😎", "💪", "👑", "💯", "🏆"],
        .heartbreak: ["💔", "😞", "🥀", "😿", "🖤"],
        .longing: ["🥺", "💭", "💞", "🌙", "⏳"],
    ]

    /// Verbs and their common forms. Shown as emoji only (no photos).
    private static let actions: [String: String] = [
        "dance": "💃", "dancing": "💃", "danced": "💃", "run": "🏃", "running": "🏃", "ran": "🏃",
        "fly": "🕊️", "flying": "🕊️", "flew": "🕊️", "drive": "🏎️", "driving": "🏎️", "drove": "🏎️",
        "sing": "🎤", "singing": "🎤", "sang": "🎤", "cry": "😭", "crying": "😭", "cried": "😭",
        "laugh": "😂", "laughing": "😂", "smile": "😊", "smiling": "😊", "sleep": "😴", "sleeping": "😴",
        "swim": "🏊", "swimming": "🏊", "jump": "🤸", "jumping": "🤸", "call": "📞", "calling": "📞",
        "pray": "🙏", "praying": "🙏", "fight": "🥊", "fighting": "🥊", "hug": "🤗", "kiss": "💋",
        "kissing": "💋", "shine": "✨", "shining": "✨", "burn": "🔥", "burning": "🔥", "fall": "🍂",
        "falling": "🍂", "wait": "⏳", "waiting": "⏳", "think": "🤔", "thinking": "🤔", "scream": "😱",
        "screaming": "😱", "whisper": "🤫", "cook": "🍳", "write": "✍️", "writing": "✍️", "ride": "🏇",
        "riding": "🏇", "climb": "🧗", "celebrate": "🥳", "walk": "🚶", "walking": "🚶", "shop": "🛍️",
        "pay": "💳", "travel": "🧳", "wink": "😉", "sweat": "😅", "kneel": "🧎", "clap": "👏",
    ]

    private static let objects: [String: String] = [
        // Sky & weather
        "moon": "🌙", "sun": "☀️", "star": "⭐", "sky": "🌌", "cloud": "☁️", "rain": "🌧️", "storm": "⛈️",
        "thunder": "⚡", "lightning": "⚡", "snow": "❄️", "rainbow": "🌈", "wind": "🌬️", "sunset": "🌅",
        "sunrise": "🌄", "night": "🌃", "planet": "🪐", "galaxy": "🌌", "comet": "☄️", "tornado": "🌪️",
        "fog": "🌫️", "heat": "🥵", "ice": "🧊", "frost": "🥶",
        // Nature
        "ocean": "🌊", "sea": "🌊", "wave": "🌊", "river": "🏞️", "lake": "🏞️", "beach": "🏖️",
        "mountain": "⛰️", "fire": "🔥", "flame": "🔥", "tree": "🌳", "forest": "🌲", "flower": "🌸",
        "rose": "🌹", "sunflower": "🌻", "tulip": "🌷", "leaf": "🍃", "island": "🏝️", "desert": "🏜️",
        "volcano": "🌋", "earth": "🌍", "world": "🌍", "cactus": "🌵", "mushroom": "🍄", "palm": "🌴",
        "rock": "🪨", "stone": "🪨", "sand": "🏖️", "shell": "🐚", "feather": "🪶", "garden": "🌷",
        "grass": "🌱", "seed": "🌱", "petal": "🌸", "wood": "🪵",
        // Animals
        "bird": "🐦", "butterfly": "🦋", "dog": "🐶", "puppy": "🐶", "cat": "🐱", "kitten": "🐱",
        "lion": "🦁", "wolf": "🐺", "snake": "🐍", "horse": "🐴", "fish": "🐟", "shark": "🦈",
        "tiger": "🐯", "bee": "🐝", "bear": "🐻", "monkey": "🐒", "owl": "🦉", "eagle": "🦅",
        "dove": "🕊️", "crow": "🐦", "raven": "🐦", "spider": "🕷️", "dragon": "🐉", "unicorn": "🦄",
        "rabbit": "🐰", "bunny": "🐰", "fox": "🦊", "deer": "🦌", "whale": "🐳", "dolphin": "🐬",
        "octopus": "🐙", "frog": "🐸", "pig": "🐷", "cow": "🐮", "chicken": "🐔", "duck": "🦆",
        "goat": "🐐", "sheep": "🐑", "mouse": "🐭", "rat": "🐀", "bat": "🦇", "turtle": "🐢",
        "penguin": "🐧", "elephant": "🐘", "zebra": "🦓", "giraffe": "🦒", "panda": "🐼", "parrot": "🦜",
        "flamingo": "🦩", "peacock": "🦚", "swan": "🦢", "jellyfish": "🐙", "crab": "🦀", "scorpion": "🦂",
        "dinosaur": "🦖", "phoenix": "🔥",
        // People & body
        "heart": "❤️", "eye": "👁️", "hand": "✋", "lip": "👄", "mouth": "👄", "nose": "👃", "ear": "👂",
        "tongue": "👅", "brain": "🧠", "bone": "🦴", "blood": "🩸", "fist": "✊", "finger": "☝️",
        "foot": "🦶", "leg": "🦵", "arm": "💪", "muscle": "💪", "crown": "👑", "angel": "😇", "devil": "😈",
        "ghost": "👻", "skull": "💀", "king": "🤴", "queen": "👸", "prince": "🤴", "princess": "👸",
        "baby": "👶", "tear": "💧", "teardrop": "💧", "zombie": "🧟", "vampire": "🧛", "witch": "🧙",
        "alien": "👽", "robot": "🤖", "clown": "🤡", "ninja": "🥷", "pirate": "☠️", "cowboy": "🤠",
        "astronaut": "🚀", "superhero": "🦸", "mermaid": "🧜", "fairy": "🧚",
        // Vehicles & places
        "car": "🚗", "plane": "✈️", "jet": "✈️", "train": "🚆", "boat": "⛵", "ship": "🚢", "bike": "🚲",
        "rocket": "🚀", "truck": "🚚", "bus": "🚌", "taxi": "🚕", "motorcycle": "🏍️", "helicopter": "🚁",
        "subway": "🚇", "spaceship": "🛸", "ufo": "🛸", "road": "🛣️", "highway": "🛣️", "street": "🛣️",
        "bridge": "🌉", "city": "🏙️", "town": "🏘️", "house": "🏠", "home": "🏡", "door": "🚪",
        "window": "🪟", "bed": "🛏️", "room": "🛋️", "couch": "🛋️", "kitchen": "🍳", "school": "🏫",
        "church": "⛪", "hospital": "🏥", "club": "🕺", "bar": "🍸", "hotel": "🏨", "castle": "🏰",
        "park": "🏞️", "airport": "🛫", "pool": "🏊", "stage": "🎭", "studio": "🎙️", "tower": "🗼",
        "prison": "⛓️", "jail": "⛓️", "farm": "🚜", "tent": "⛺", "factory": "🏭", "office": "🏢",
        "bank": "🏦", "stadium": "🏟️", "temple": "🛕", "palace": "🏰", "skyscraper": "🏙️",
        // Food & drink
        "bottle": "🍾", "wine": "🍷", "champagne": "🍾", "beer": "🍺", "drink": "🍹", "cocktail": "🍸",
        "coffee": "☕", "tea": "🍵", "milk": "🥛", "water": "💧", "cake": "🎂", "candy": "🍬",
        "cherry": "🍒", "apple": "🍎", "pizza": "🍕", "burger": "🍔", "fries": "🍟", "chocolate": "🍫",
        "strawberry": "🍓", "lemon": "🍋", "peach": "🍑", "banana": "🍌", "grape": "🍇", "bread": "🍞",
        "honey": "🍯", "cookie": "🍪", "donut": "🍩", "popcorn": "🍿", "cheese": "🧀", "egg": "🥚",
        "taco": "🌮", "sushi": "🍣", "noodle": "🍜", "rice": "🍚", "pie": "🥧", "lollipop": "🍭",
        "watermelon": "🍉", "pineapple": "🍍", "coconut": "🥥", "mango": "🥭", "avocado": "🥑",
        "pepper": "🌶️", "salt": "🧂", "sugar": "🍬", "icecream": "🍦", "juice": "🧃", "whiskey": "🥃",
        "tequila": "🥃", "vodka": "🥃",
        // Clothes & style
        "glasses": "👓", "sunglasses": "🕶️", "shoe": "👟", "sneaker": "👟", "boot": "👢", "heel": "👠",
        "dress": "👗", "hat": "🎩", "cap": "🧢", "jacket": "🧥", "coat": "🧥", "jeans": "👖", "shirt": "👕",
        "lipstick": "💄", "makeup": "💄", "perfume": "🌸", "necklace": "📿", "bikini": "👙", "tie": "👔",
        "scarf": "🧣", "glove": "🧤", "sock": "🧦", "purse": "👛", "bag": "👜", "backpack": "🎒",
        "suitcase": "🧳", "umbrella": "☂️",
        // Things & tech
        "phone": "📱", "money": "💵", "cash": "💸", "dollar": "💵", "coin": "🪙", "wallet": "👛",
        "diamond": "💎", "gold": "🥇", "silver": "🥈", "ring": "💍", "key": "🔑", "clock": "⏰",
        "watch": "⌚", "gun": "🔫", "knife": "🔪", "sword": "🗡️", "shield": "🛡️", "bow": "🏹",
        "arrow": "🏹", "bomb": "💣", "guitar": "🎸", "piano": "🎹", "drum": "🥁", "violin": "🎻",
        "trumpet": "🎺", "saxophone": "🎷", "microphone": "🎤", "mic": "🎤", "radio": "📻", "music": "🎵",
        "song": "🎶", "record": "💿", "vinyl": "💿", "camera": "📷", "video": "📹", "movie": "🎬",
        "film": "🎞️", "tv": "📺", "television": "📺", "computer": "💻", "laptop": "💻",
        "headphones": "🎧", "speaker": "🔊", "light": "💡", "lamp": "💡", "battery": "🔋",
        "satellite": "🛰️", "controller": "🎮", "letter": "✉️", "envelope": "✉️", "book": "📖",
        "paper": "📄", "pen": "🖊️", "pencil": "✏️", "paint": "🎨", "brush": "🖌️", "scissors": "✂️",
        "hammer": "🔨", "rope": "🪢", "ladder": "🪜", "bell": "🔔", "anchor": "⚓", "kite": "🪁",
        "puzzle": "🧩", "dice": "🎲", "card": "🃏", "gift": "🎁", "present": "🎁", "balloon": "🎈",
        "candle": "🕯️", "mirror": "🪞", "chain": "⛓️", "lock": "🔒", "magic": "✨", "party": "🎉",
        "ticket": "🎟️", "map": "🗺️", "compass": "🧭", "smoke": "💨", "flag": "🚩", "trophy": "🏆",
        "medal": "🏅", "pill": "💊", "medicine": "💊", "cigarette": "🚬", "telescope": "🔭",
        "crystal": "🔮", "magnet": "🧲", "candlelight": "🕯️", "firework": "🎆", "fireworks": "🎆",
        "confetti": "🎊", "disco": "🕺", "spotlight": "🔦", "flashlight": "🔦", "tattoo": "🖋️",
        "pillow": "🛏️", "blanket": "🛌", "chair": "🪑", "table": "🍽️", "glass": "🥂", "cup": "☕",
        "plate": "🍽️", "box": "📦", "photo": "🖼️", "picture": "🖼️", "painting": "🖼️", "statue": "🗽",
    ]

    private static let abstract: Set<String> = [
        "time", "life", "way", "thing", "feeling", "mind", "soul", "truth", "lie", "memory", "reason",
        "chance", "nothing", "everything", "something", "anything", "moment", "place", "end", "beginning",
        "love", "hate", "pain", "fear", "hope", "day", "tonight", "today", "tomorrow", "yesterday",
        "dream", "word", "story", "game", "lesson", "side", "part", "kind", "lot",
    ]

    public static var objectCount: Int { objects.count }

    /// Every emoji Apmuly can show (for compatibility checks).
    static var allEmoji: [String] {
        Array(Set(emotions.values.flatMap { $0 } + actions.values + objects.values)).sorted()
    }
    public static var actionCount: Int { actions.count }

    public static func emoji(for emotion: Emotion, variant: Int) -> String? {
        guard let options = emotions[emotion], !options.isEmpty else { return nil }
        return options[abs(variant) % options.count]
    }

    public static func emoji(forObject word: String) -> String? {
        let key = normalize(word)
        guard !key.isEmpty, !isAbstract(key) else { return nil }
        return candidates(key).lazy.compactMap { objects[$0] }.first
    }

    /// Emoji for an action word ("dancing" → 💃). Exact forms only.
    public static func emoji(forAction word: String) -> String? {
        actions[normalize(word)]
    }

    public static func isAbstract(_ word: String) -> Bool {
        candidates(normalize(word)).contains { abstract.contains($0) }
    }

    /// Lowercased, letters only, singular where a lexicon entry exists.
    public static func canonical(_ word: String) -> String {
        let key = normalize(word)
        return candidates(key).first { objects[$0] != nil || abstract.contains($0) } ?? singular(key)
    }

    static func normalize(_ word: String) -> String {
        String(word.lowercased().filter { $0.isLetter || $0 == " " || $0 == "-" })
            .trimmingCharacters(in: .whitespaces)
    }

    /// The word itself, then simple singular forms (cities → city, boxes → box, roses → rose).
    static func candidates(_ key: String) -> [String] {
        var forms = [key]
        if key.hasSuffix("ies"), key.count > 4 { forms.append(String(key.dropLast(3)) + "y") }
        if key.hasSuffix("es"), key.count > 3 { forms.append(String(key.dropLast(2))) }
        if key.hasSuffix("s"), !key.hasSuffix("ss"), key.count > 2 { forms.append(String(key.dropLast())) }
        return forms
    }

    private static func singular(_ key: String) -> String {
        if key.hasSuffix("ies"), key.count > 4 { return String(key.dropLast(3)) + "y" }
        if key.hasSuffix("s"), !key.hasSuffix("ss"), key.count > 3 { return String(key.dropLast()) }
        return key
    }
}
