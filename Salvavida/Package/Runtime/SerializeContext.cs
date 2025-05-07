namespace Salvavida
{
    public class SerializeContext : IJobJoinable
    {

        public PathBuilder Path { get; internal set; }
        public bool UniqueLocked { get; internal set; }

        protected int _joinableSignatrue;

        public virtual void GetFromPool()
        {
            _joinableSignatrue = SvHelper.randomizer.Next();
        }

        public virtual void ReturnToPool()
        {

        }

        public virtual int GetJoinableSignature() => _joinableSignatrue;
    }
}
