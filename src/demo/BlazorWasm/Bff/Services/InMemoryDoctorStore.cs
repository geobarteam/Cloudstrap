namespace Cloudstrap.WasmTestProject.Host.Bff.Services
{
    using Cloudstrap.WasmTestProject.Contracts;

    /// <summary>
    /// Seeded, thread-safe in-memory doctor store — the SUT takes no database dependency until
    /// a deliverable's demo needs one.
    /// </summary>
    public sealed class InMemoryDoctorStore
    {
        private readonly object _lock = new object();
        private readonly List<DoctorDto> _doctors =
        [
            new DoctorDto(1, "Dr. Alice Carter", "Cardiology"),
            new DoctorDto(2, "Dr. Ben Okafor", "Neurology"),
            new DoctorDto(3, "Dr. Chloe Martin", "Pediatrics"),
        ];

        private int _nextId = 4;

        public IReadOnlyList<DoctorDto> GetAll()
        {
            lock (_lock)
            {
                return [.. _doctors];
            }
        }

        public DoctorDto Add(AddDoctorDto doctor)
        {
            ArgumentNullException.ThrowIfNull(doctor);

            lock (_lock)
            {
                DoctorDto created = new DoctorDto(_nextId++, doctor.Name, doctor.Specialty);
                _doctors.Add(created);

                return created;
            }
        }
    }
}
