namespace Cloudstrap.BlazorWasm
{
    /// <summary>
    /// The wire DTO of the BFF user endpoint's response — internal, deserialization only
    /// (camelCase JSON, read case-insensitively).
    /// </summary>
    internal sealed class UserInfo
    {
        /// <summary>Gets or sets a value indicating whether the user is authenticated.</summary>
        public bool IsAuthenticated
        {
            get; set;
        }

        /// <summary>Gets or sets the authenticated user's name.</summary>
        public string? UserName
        {
            get; set;
        }

        /// <summary>Gets or sets the authenticated user's claims; may be omitted when anonymous.</summary>
        public List<ClaimDto>? Claims
        {
            get; set;
        }
    }
}
