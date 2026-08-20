namespace Cloudstrap.WasmTestProject.Contracts
{
    /// <summary>
    /// The payload for adding a doctor.
    /// </summary>
    public sealed record AddDoctorDto(string Name, string Specialty);
}
