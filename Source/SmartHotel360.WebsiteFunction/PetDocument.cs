using System;
using System.Text.Json.Serialization;

namespace PetCheckerFunction
{
    public class PetDocument
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        public string? PetName { get; set; }

        public string? MediaUrl { get; set; }

        public bool? IsApproved { get; set; }

        public DateTime Created { get; set; }

        public DateTime? Analyzed { get; set; }

        public string? Message { get; set; }
    }
}