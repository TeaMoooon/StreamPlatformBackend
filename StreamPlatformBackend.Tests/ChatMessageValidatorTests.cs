using StreamPlatformBackend.Helpers;
using Xunit;

namespace StreamPlatformBackend.Tests
{
    public class ChatMessageValidatorTests
    {
        [Theory]
        [InlineData("😀")]
        [InlineData("🔥💯")]
        [InlineData("👍 👏")]
        [InlineData("😀🔥 ❤️")]
        [InlineData("🫡")]
        [InlineData("❤️")]
        public void IsEmoteOnlyMessage_AllowsEmojiOnly(string text)
        {
            Assert.True(ChatMessageValidator.IsEmoteOnlyMessage(text));
        }

        [Theory]
        [InlineData("hello")]
        [InlineData("привет")]
        [InlineData("😀 ok")]
        [InlineData("")]
        [InlineData("   ")]
        public void IsEmoteOnlyMessage_RejectsText(string text)
        {
            Assert.False(ChatMessageValidator.IsEmoteOnlyMessage(text));
        }
    }
}
