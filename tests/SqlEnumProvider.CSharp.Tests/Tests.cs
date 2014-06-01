using Xunit;
using System;

namespace FSharp.Data
{
    public class Tests
    {
        [Fact]
        public void TryParse()
        {
            var result = -1;
            Assert.True( IntMapping.TryParse("One", ref result));
            Assert.Equal(IntMapping.One, result);

            Assert.True(IntMapping.TryParse("tWo", ignoreCase: true, result: ref result));
            Assert.Equal(IntMapping.Two, result);

            Assert.True(IntMapping.TryParse("Two", ref result));
            Assert.Equal(IntMapping.Two, result);

            Assert.False(IntMapping.TryParse("tWO", ref result));
        }
    }
}
