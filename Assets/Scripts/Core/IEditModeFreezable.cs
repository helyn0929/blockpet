namespace BlockPet.Core
{
    /// <summary>
    /// Implement on any MonoBehaviour that should pause when room edit mode is active.
    /// </summary>
    public interface IEditModeFreezable
    {
        void SetEditModeFrozen(bool frozen);
    }
}
