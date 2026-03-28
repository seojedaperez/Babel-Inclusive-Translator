namespace ICH.AIPipeline.Pipeline;

/// <summary>
/// Sign language animation dictionary.
/// Maps words to animation IDs for the visual sign language rendering.
/// 
/// In production, this would be backed by a comprehensive database of
/// sign language animations (ASL, BSL, etc.).
/// </summary>
public static class SignLanguageDictionary
{
    /// <summary>
    /// Common word → animation ID mapping.
    /// These IDs correspond to Lottie/SkiaSharp animation assets.
    /// </summary>
    private static readonly Dictionary<string, string> _animations = new(StringComparer.OrdinalIgnoreCase)
    {
        // Greetings
        ["hello"] = "asl_hello",
        ["hi"] = "asl_hello",
        ["goodbye"] = "asl_goodbye",
        ["bye"] = "asl_goodbye",
        ["thanks"] = "asl_thanks",
        ["thank"] = "asl_thanks",
        ["please"] = "asl_please",
        ["sorry"] = "asl_sorry",
        ["welcome"] = "asl_welcome",

        // Common words
        ["yes"] = "asl_yes",
        ["no"] = "asl_no",
        ["help"] = "asl_help",
        ["stop"] = "asl_stop",
        ["go"] = "asl_go",
        ["come"] = "asl_come",
        ["want"] = "asl_want",
        ["need"] = "asl_need",
        ["like"] = "asl_like",
        ["love"] = "asl_love",
        ["good"] = "asl_good",
        ["bad"] = "asl_bad",
        ["happy"] = "asl_happy",
        ["sad"] = "asl_sad",

        // Questions
        ["what"] = "asl_what",
        ["where"] = "asl_where",
        ["when"] = "asl_when",
        ["who"] = "asl_who",
        ["why"] = "asl_why",
        ["how"] = "asl_how",

        // People
        ["i"] = "asl_i",
        ["me"] = "asl_me",
        ["you"] = "asl_you",
        ["we"] = "asl_we",
        ["they"] = "asl_they",
        ["he"] = "asl_he",
        ["she"] = "asl_she",

        // Actions
        ["eat"] = "asl_eat",
        ["drink"] = "asl_drink",
        ["sleep"] = "asl_sleep",
        ["work"] = "asl_work",
        ["play"] = "asl_play",
        ["learn"] = "asl_learn",
        ["understand"] = "asl_understand",
        ["know"] = "asl_know",
        ["think"] = "asl_think",
        ["feel"] = "asl_feel",
        ["see"] = "asl_see",
        ["hear"] = "asl_hear",
        ["speak"] = "asl_speak",
        ["talk"] = "asl_talk",
        ["listen"] = "asl_listen",
        ["read"] = "asl_read",
        ["write"] = "asl_write",

        // Time
        ["today"] = "asl_today",
        ["tomorrow"] = "asl_tomorrow",
        ["yesterday"] = "asl_yesterday",
        ["now"] = "asl_now",
        ["later"] = "asl_later",
        ["morning"] = "asl_morning",
        ["afternoon"] = "asl_afternoon",
        ["night"] = "asl_night",

        // Numbers (fingerspelling)
        ["one"] = "asl_1",
        ["two"] = "asl_2",
        ["three"] = "asl_3",
        ["four"] = "asl_4",
        ["five"] = "asl_5",

        // Common phrases mapped to compound animations
        ["okay"] = "asl_ok",
        ["ok"] = "asl_ok",
        ["fine"] = "asl_fine",
        ["wait"] = "asl_wait",
        ["again"] = "asl_again",
        ["more"] = "asl_more",
        ["finished"] = "asl_finished",
        ["done"] = "asl_finished",

        // Meeting-related
        ["meeting"] = "asl_meeting",
        ["question"] = "asl_question",
        ["answer"] = "asl_answer",
        ["agree"] = "asl_agree",
        ["disagree"] = "asl_disagree",
        ["important"] = "asl_important",
        ["idea"] = "asl_idea",
        ["problem"] = "asl_problem",
        ["solution"] = "asl_solution"
    };

    /// <summary>
    /// Letters for fingerspelling unknown words.
    /// </summary>
    private static readonly Dictionary<char, string> _alphabet = new()
    {
        ['a'] = "asl_a", ['b'] = "asl_b", ['c'] = "asl_c", ['d'] = "asl_d",
        ['e'] = "asl_e", ['f'] = "asl_f", ['g'] = "asl_g", ['h'] = "asl_h",
        ['i'] = "asl_i_letter", ['j'] = "asl_j", ['k'] = "asl_k", ['l'] = "asl_l",
        ['m'] = "asl_m", ['n'] = "asl_n", ['o'] = "asl_o", ['p'] = "asl_p",
        ['q'] = "asl_q", ['r'] = "asl_r", ['s'] = "asl_s", ['t'] = "asl_t",
        ['u'] = "asl_u", ['v'] = "asl_v", ['w'] = "asl_w", ['x'] = "asl_x",
        ['y'] = "asl_y", ['z'] = "asl_z"
    };

    /// <summary>
    /// Get the animation ID for a word.
    /// If not found, returns a fingerspelling sequence.
    /// </summary>
    public static string GetAnimationId(string word)
    {
        if (_animations.TryGetValue(word, out var animId))
            return animId;

        // For unknown words, return fingerspell prefix
        return $"fingerspell_{word.ToLowerInvariant()}";
    }

    /// <summary>
    /// Get fingerspelling animation IDs for a word.
    /// </summary>
    public static IReadOnlyList<string> GetFingerspellingIds(string word)
    {
        var result = new List<string>();
        foreach (var c in word.ToLowerInvariant())
        {
            if (_alphabet.TryGetValue(c, out var letterAnim))
                result.Add(letterAnim);
        }
        return result;
    }

    /// <summary>
    /// Check if a word has a direct sign animation.
    /// </summary>
    public static bool HasAnimation(string word) =>
        _animations.ContainsKey(word);

    /// <summary>
    /// Get all available animations.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetAllAnimations() =>
        _animations;
}
