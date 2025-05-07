namespace Salvavida
{
    public class SerializeContext : IJobJoinable
    {
        public PathBuilder Path { get; internal set; }
        public bool UniqueLocked { get; internal set; }

        public virtual int GetJoinableSignature() => GetHashCode();
    }
}
