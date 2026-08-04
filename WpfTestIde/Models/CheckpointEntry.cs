using System;
using System.Collections.Generic;
using System.Linq;

namespace WpfTestIde.Models
{
    /// <summary>
    /// Represents a checkpoint for test verification.
    /// Checkpoints capture expected state at recording time and verify it at playback.
    /// </summary>
    public class CheckpointEntry
    {
        /// <summary>
        /// Type of checkpoint.
        /// </summary>
        public CheckpointType Type { get; set; } = CheckpointType.Property;

        /// <summary>
        /// Unique identifier for this checkpoint.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

        /// <summary>
        /// Alias of the element this checkpoint targets (e.g., "LoginPage.btnSubmit").
        /// </summary>
        public string? ElementAlias { get; set; }

        /// <summary>
        /// The property being verified (e.g., "Text", "IsEnabled", "IsVisible").
        /// </summary>
        public string PropertyName { get; set; } = "Text";

        /// <summary>
        /// Expected value at the time of recording.
        /// </summary>
        public string ExpectedValue { get; set; } = "";

        /// <summary>
        /// Additional parameters (e.g., tolerance for numeric comparisons).
        /// </summary>
        public Dictionary<string, string> Parameters { get; set; } = new();

        /// <summary>
        /// Timestamp when the checkpoint was created.
        /// </summary>
        public string CreatedAt { get; set; } = DateTime.Now.ToString("o");

        /// <summary>
        /// Optional description of what this checkpoint verifies.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Screen coordinates for area/image checkpoints.
        /// </summary>
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }

        /// <summary>
        /// Baseline screenshot path for image checkpoints.
        /// </summary>
        public string? BaselineImagePath { get; set; }

        /// <summary>
        /// Converts to YAML-compatible dictionary.
        /// </summary>
        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>
            {
                ["id"] = Id,
                ["type"] = Type.ToString(),
                ["propertyName"] = PropertyName,
                ["expectedValue"] = ExpectedValue,
                ["createdAt"] = CreatedAt
            };

            if (!string.IsNullOrEmpty(ElementAlias))
                dict["elementAlias"] = ElementAlias;

            if (!string.IsNullOrEmpty(Description))
                dict["description"] = Description;

            if (X.HasValue && Y.HasValue)
            {
                dict["x"] = X.Value;
                dict["y"] = Y.Value;
            }

            if (Width.HasValue && Height.HasValue)
            {
                dict["width"] = Width.Value;
                dict["height"] = Height.Value;
            }

            if (!string.IsNullOrEmpty(BaselineImagePath))
                dict["baselineImagePath"] = BaselineImagePath;

            if (Parameters.Any())
                dict["parameters"] = Parameters;

            return dict;
        }
    }

    /// <summary>
    /// Types of checkpoints supported.
    /// </summary>
    public enum CheckpointType
    {
        /// <summary>
        /// Verifies an element property value (Text, IsEnabled, IsVisible, etc.).
        /// </summary>
        Property,

        /// <summary>
        /// Verifies content in a screen area using OCR.
        /// </summary>
        Area,

        /// <summary>
        /// Verifies element appearance using image comparison.
        /// </summary>
        Image,

        /// <summary>
        /// Verifies DataGrid content.
        /// </summary>
        DataGrid,

        /// <summary>
        /// Verifies element count in a container.
        /// </summary>
        Count,

        /// <summary>
        /// Verifies a specific attribute value.
        /// </summary>
        Attribute
    }

    /// <summary>
    /// Comparison operators for checkpoint verification.
    /// </summary>
    public enum ComparisonOperator
    {
        Equals,
        NotEquals,
        Contains,
        StartsWith,
        EndsWith,
        GreaterThan,
        LessThan,
        GreaterThanOrEqual,
        LessThanOrEqual,
        MatchesRegex
    }
}
