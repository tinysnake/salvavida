using Xunit;
using System.Collections.Generic;

namespace Salvavida.Tests
{
    [Savable]
    public partial class TestClassDontSupportLazyLoad
    {
        [LazyLoad]

        private SavableCustomData? _savableCustomData;

        [LazyLoad]

        private List<int>? _testIntList;

        [LazyLoad]
        private List<SavableCustomData>? _savableCustomDataList;
    }
}