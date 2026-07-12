using System.Globalization;
using System.Text;

namespace StreamPlatformBackend.Helpers
{
    public static class ChatMessageValidator
    {
        public static bool IsEmoteOnlyMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var hasContent = false;
            var elements = StringInfo.GetTextElementEnumerator(text);
            while (elements.MoveNext())
            {
                var grapheme = elements.GetTextElement();
                if (string.IsNullOrWhiteSpace(grapheme))
                    continue;

                hasContent = true;
                if (!IsEmojiGrapheme(grapheme))
                    return false;
            }

            return hasContent;
        }

        private static bool IsEmojiGrapheme(string grapheme)
        {
            foreach (var rune in grapheme.EnumerateRunes())
            {
                if (rune.Value is '\uFE0F' or '\u200D')
                    continue;

                if (!IsEmojiCodePoint(rune.Value))
                    return false;
            }

            return true;
        }

        private static bool IsEmojiCodePoint(int codePoint) =>
            (codePoint >= 0x1F1E6 && codePoint <= 0x1F1FF) ||
            (codePoint >= 0x1F300 && codePoint <= 0x1FAFF) ||
            (codePoint >= 0x2600 && codePoint <= 0x26FF) ||
            (codePoint >= 0x2700 && codePoint <= 0x27BF) ||
            (codePoint >= 0x2300 && codePoint <= 0x23FF) ||
            (codePoint >= 0x2B00 && codePoint <= 0x2BFF) ||
            codePoint is 0x2764 or 0x2763;
    }
}
