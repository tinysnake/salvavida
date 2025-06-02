namespace Salvavida
{
    public class SerializeContext
    {

        public PathBuilder Path { get; internal set; }
        public bool UniqueLocked { get; internal set; }

        public virtual void GetFromPool()
        {
        }

        public virtual void ReturnToPool()
        {

        }

        public override int GetHashCode()
        {
            if (Path == null)
                return 0;
            return SvHelper.GetHashCodeFromSpan(Path.AsSpan());
        }
    }
}
