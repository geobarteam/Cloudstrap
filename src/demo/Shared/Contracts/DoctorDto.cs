namespace Cloudstrap.Demo.Contracts
{
    /// <summary>
    /// A doctor as served by the Bff API.
    /// </summary>
    public sealed record DoctorDto(int Id, string Name, string Specialty);
}
