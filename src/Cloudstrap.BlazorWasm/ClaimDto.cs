namespace Cloudstrap.BlazorWasm
{
    /// <summary>
    /// One claim on the BFF user endpoint's wire contract — internal, deserialization only.
    /// </summary>
    internal sealed class ClaimDto
    {
        /// <summary>Gets or sets the claim type (for example <c>sub</c> or <c>email</c>).</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Gets or sets the claim value.</summary>
        public string Value { get; set; } = string.Empty;
    }
}
