using Xunit;
using Salvavida;

namespace Salvavida.Tests
{
    /// <summary>
    /// Domain-style interface that is NOT ISavable-derived itself
    /// (mirrors Babybus SimWorld's IEntityDataSection usage pattern).
    /// </summary>
    public interface ITestEntitySection : ISavable
    {
    }

    /// <summary>
    /// A [Savable] class implementing a domain interface. The generator must emit a bridge
    /// implementation of ISavable&lt;ITestEntitySection&gt; so that
    /// ObservableCollection.TryWatch pattern matching succeeds when the collection element
    /// type is the interface, not the concrete class.
    /// </summary>
    [Savable]
    [UseSystemTextJson]
    public partial class BridgeSectionData : ITestEntitySection
    {
        public Dictionary<string, string>? values;
    }

    [Savable]
    [UseSystemTextJson]
    public partial class BridgeHostData
    {
        public List<ITestEntitySection>? sections;
    }

    public class BridgeInterfaceTests
    {
        [Fact]
        public void SavableClass_WithDomainInterface_ImplementsISavableOfInterface()
        {
            var section = new BridgeSectionData();
            Assert.True(section is ISavable<ITestEntitySection>);
            // Self-typed implementation must be preserved.
            Assert.True(section is ISavable<BridgeSectionData>);
        }

        [Fact]
        public void SavableClass_WithoutDomainInterface_DoesNotBridge()
        {
            // BridgeSectionData must not accidentally bridge ISavable<BridgeHostData> etc.
            var section = new BridgeSectionData();
            Assert.False(section is ISavable<BridgeHostData>);
        }

        [Fact]
        public void SectionAddedToInterfaceList_GetsParentFromCollection()
        {
            var host = new BridgeHostData();
            host.sections = new List<ITestEntitySection>();
            var section = new BridgeSectionData();
            host.Sections!.Add(section);

            var sv = (ISavable)section;
            Assert.NotNull(sv.SvParent);
            Assert.Same(host.Sections, sv.SvParent);
        }

        [Fact]
        public void SectionContentChange_PropagatesToHost()
        {
            var host = new BridgeHostData();
            host.sections = new List<ITestEntitySection>();
            var section = new BridgeSectionData();
            host.Sections!.Add(section);

            ((ISavable)host).SetDirty(false, true);
            Assert.False(host.IsDirty);

            section.values = new Dictionary<string, string>();
            section.Values!["glasses"] = "Glasses_07";

            Assert.True(section.IsDirty);
            Assert.True(host.IsDirty);
        }

        [Fact]
        public void SectionPropertyChange_PropagatesToHost()
        {
            var host = new BridgeHostData();
            host.sections = new List<ITestEntitySection>();
            var section = new BridgeSectionData();
            host.Sections!.Add(section);

            ((ISavable)host).SetDirty(false, true);
            Assert.False(host.IsDirty);

            section.values = new Dictionary<string, string>();
            section.SetValues(new Dictionary<string, string> { ["glasses"] = "Glasses_07" });

            Assert.True(host.IsDirty);
        }
    }
}
