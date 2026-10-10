namespace RackLoader.Contracts;

/// <summary>Entry point implemented by a mod assembly named in the .rkm manifest.</summary>
public interface IRackMod
{
    void Load(IRackModContext context);
}
