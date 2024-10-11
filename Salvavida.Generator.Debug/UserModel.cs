#nullable disable

namespace Salvavida.Generator.Debug
{
    public partial class UserModel
    {
        private int _index;
        private string _guid;
        private bool _isActive;
        private int _age;
        private float _salary;
        private string[] _tags;
        private UserModel _child;
        private List<HouseModel> _houses;
        [SaveSeparately]
        private List<Friend> _friends;
        private Dictionary<string, Friend> _friendsDict;
    }

    public partial class UserModel : ISavable<UserModel>
    {
        public event PropertyChangeEventHandler<UserModel> PropertyChanged;

        public ISavable Parent { get; private set; }

        public string SvId { get; set; }

        public bool IsDirty { get; private set; }

        public void AfterDeserialize(Serializer serializer, PathBuilder path)
        {
        }

        public void AfterSerialize(Serializer serializer, PathBuilder path)
        {
        }

        public void BeforeSerialize(Serializer serializer)
        {
        }

        public void Invalidate(bool recursively)
        {
            SetDirty(true, recursively);
            var serializer = this.GetSerializer();
            if (serializer == null)
                return;

            if (string.IsNullOrEmpty(SvId))
                throw new ArgumentNullException(nameof(SvId));

            if (this is ISerializeRoot root)
                serializer.FreshSaveByPolicy(this);
            else
                PropertyChanged?.Invoke(this, SvId);
        }

        public void SetDirty(bool dirty, bool recursively)
        {
            IsDirty = dirty;
            if (recursively)
            {
                //friends.SetDirty(dirty, recursively);
            }
        }

        public void SetParent(ISavable parent)
        {
            Parent = parent;
        }
    }

    public partial class Profile1
    {
        private string _picture;
        private string _email;
    }

    public class Profile2
    {
        public string abc;
        public string def;
    }

    public partial class HouseModel
    {
        private string _address;
        private int _size;
        private float _price;
    }

    public class Friend
    {
        public string guid;
        public string name;
    }
}
