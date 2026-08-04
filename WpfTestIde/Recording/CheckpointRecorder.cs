using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using WpfTestIde.Models;

namespace WpfTestIde.Recording
{
    /// <summary>
    /// Captures checkpoints during recording sessions.
    /// Provides point-and-click checkpoint creation for non-programmers.
    /// </summary>
    public class CheckpointRecorder
    {
        private readonly List<CheckpointEntry> _checkpoints = new();
        private readonly string _pipeName;
        private readonly string _checkpointDir;
        private readonly string _baselineImagesDir;
        private readonly SpyAgentClient _client;

        public CheckpointRecorder(string pipeName = "WPFSpyAgentPipe", string checkpointDir = "repository/checkpoints")
        {
            _pipeName = pipeName;
            _checkpointDir = checkpointDir;
            _baselineImagesDir = Path.Combine(checkpointDir, "baseline_images");
            _client = new SpyAgentClient(_pipeName);

            Directory.CreateDirectory(_checkpointDir);
            Directory.CreateDirectory(_baselineImagesDir);
        }

        /// <summary>
        /// Gets all captured checkpoints.
        /// </summary>
        public IReadOnlyList<CheckpointEntry> Checkpoints => _checkpoints.AsReadOnly();

        /// <summary>
        /// Creates a property checkpoint for an element.
        /// </summary>
        public CheckpointEntry CreatePropertyCheckpoint(
            string elementAlias,
            string propertyName,
            string? expectedValue = null,
            string description = "")
        {
            // Get current value from element
            string actualValue = "";
            if (string.IsNullOrEmpty(expectedValue))
            {
                actualValue = GetPropertyValue(elementAlias, propertyName);
            }
            else
            {
                actualValue = expectedValue;
            }

            var checkpoint = new CheckpointEntry
            {
                Type = CheckpointType.Property,
                ElementAlias = elementAlias,
                PropertyName = propertyName,
                ExpectedValue = actualValue,
                Description = string.IsNullOrEmpty(description) 
                    ? $"Verify {propertyName} of {elementAlias}"
                    : description
            };

            _checkpoints.Add(checkpoint);
            Log($"[CheckpointRecorder] Created property checkpoint: {checkpoint.Id} - {elementAlias}.{propertyName} = '{actualValue}'");
            return checkpoint;
        }

        /// <summary>
        /// Creates an area checkpoint by capturing OCR text in a screen region.
        /// </summary>
        public CheckpointEntry CreateAreaCheckpoint(
            double x, double y, double width, double height,
            string? expectedText = null,
            string description = "")
        {
            // Capture screenshot of the area
            string? baselinePath = CaptureAreaImage(x, y, width, height);

            // Get OCR text from the area
            string ocrText = GetOcrText(x, y, width, height);

            var checkpoint = new CheckpointEntry
            {
                Type = CheckpointType.Area,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                ExpectedValue = expectedText ?? ocrText,
                BaselineImagePath = baselinePath,
                Description = string.IsNullOrEmpty(description)
                    ? $"Verify area text at ({x},{y})"
                    : description
            };

            _checkpoints.Add(checkpoint);
            Log($"[CheckpointRecorder] Created area checkpoint: {checkpoint.Id} at ({x},{y},{width},{height})");
            return checkpoint;
        }

        /// <summary>
        /// Creates an image checkpoint for visual verification.
        /// </summary>
        public CheckpointEntry CreateImageCheckpoint(
            double x, double y, double width, double height,
            string? baselineImagePath = null,
            string description = "")
        {
            // Capture screenshot for baseline
            string path = baselineImagePath ?? CaptureAreaImage(x, y, width, height);

            var checkpoint = new CheckpointEntry
            {
                Type = CheckpointType.Image,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                BaselineImagePath = path,
                Description = string.IsNullOrEmpty(description)
                    ? $"Visual verification at ({x},{y})"
                    : description
            };

            _checkpoints.Add(checkpoint);
            Log($"[CheckpointRecorder] Created image checkpoint: {checkpoint.Id} with baseline: {path}");
            return checkpoint;
        }

        /// <summary>
        /// Creates a DataGrid content checkpoint.
        /// </summary>
        public CheckpointEntry CreateDataGridCheckpoint(
            string elementAlias,
            string? expectedContent = null,
            string description = "")
        {
            // Get current DataGrid content
            string content = GetDataGridContent(elementAlias);

            var checkpoint = new CheckpointEntry
            {
                Type = CheckpointType.DataGrid,
                ElementAlias = elementAlias,
                ExpectedValue = expectedContent ?? content,
                Description = string.IsNullOrEmpty(description)
                    ? $"Verify DataGrid content in {elementAlias}"
                    : description
            };

            _checkpoints.Add(checkpoint);
            Log($"[CheckpointRecorder] Created DataGrid checkpoint: {checkpoint.Id} for {elementAlias}");
            return checkpoint;
        }

        /// <summary>
        /// Creates an attribute checkpoint.
        /// </summary>
        public CheckpointEntry CreateAttributeCheckpoint(
            string elementAlias,
            string attributeName,
            string? expectedValue = null,
            string description = "")
        {
            string actualValue = expectedValue ?? GetAttributeValue(elementAlias, attributeName);

            var checkpoint = new CheckpointEntry
            {
                Type = CheckpointType.Attribute,
                ElementAlias = elementAlias,
                PropertyName = attributeName,
                ExpectedValue = actualValue,
                Description = string.IsNullOrEmpty(description)
                    ? $"Verify {attributeName} of {elementAlias}"
                    : description
            };

            _checkpoints.Add(checkpoint);
            Log($"[CheckpointRecorder] Created attribute checkpoint: {checkpoint.Id} - {elementAlias}[{attributeName}] = '{actualValue}'");
            return checkpoint;
        }

        /// <summary>
        /// Creates a count checkpoint for container elements.
        /// </summary>
        public CheckpointEntry CreateCountCheckpoint(
            string elementAlias,
            int expectedCount,
            string description = "")
        {
            var checkpoint = new CheckpointEntry
            {
                Type = CheckpointType.Count,
                ElementAlias = elementAlias,
                ExpectedValue = expectedCount.ToString(),
                Description = string.IsNullOrEmpty(description)
                    ? $"Verify {elementAlias} count = {expectedCount}"
                    : description
            };

            _checkpoints.Add(checkpoint);
            Log($"[CheckpointRecorder] Created count checkpoint: {checkpoint.Id} - {elementAlias} count = {expectedCount}");
            return checkpoint;
        }

        /// <summary>
        /// Removes a checkpoint by ID.
        /// </summary>
        public bool RemoveCheckpoint(string checkpointId)
        {
            var checkpoint = _checkpoints.FirstOrDefault(c => c.Id == checkpointId);
            if (checkpoint != null)
            {
                _checkpoints.Remove(checkpoint);
                Log($"[CheckpointRecorder] Removed checkpoint: {checkpointId}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Updates an existing checkpoint.
        /// </summary>
        public bool UpdateCheckpoint(string checkpointId, Action<CheckpointEntry> updateAction)
        {
            var checkpoint = _checkpoints.FirstOrDefault(c => c.Id == checkpointId);
            if (checkpoint != null)
            {
                updateAction(checkpoint);
                Log($"[CheckpointRecorder] Updated checkpoint: {checkpointId}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Exports checkpoints to a YAML file.
        /// </summary>
        public void Export(string? filePath = null)
        {
            filePath ??= Path.Combine(_checkpointDir, $"checkpoints_{DateTime.Now:yyyyMMdd_HHmmss}.yaml");
            
            var lines = new List<string>
            {
                "# Checkpoint Definitions",
                $"# Generated: {DateTime.Now:o}",
                "# Add these to your test repository",
                "",
                "checkpoints:"
            };

            foreach (var cp in _checkpoints)
            {
                lines.Add($"  - id: {cp.Id}");
                lines.Add($"    type: {cp.Type}");
                if (!string.IsNullOrEmpty(cp.ElementAlias))
                    lines.Add($"    elementAlias: {cp.ElementAlias}");
                lines.Add($"    propertyName: {cp.PropertyName}");
                lines.Add($"    expectedValue: \"{cp.ExpectedValue}\"");
                if (!string.IsNullOrEmpty(cp.Description))
                    lines.Add($"    description: \"{cp.Description}\"");
                
                if (cp.X.HasValue)
                    lines.Add($"    x: {cp.X.Value}");
                if (cp.Y.HasValue)
                    lines.Add($"    y: {cp.Y.Value}");
                if (cp.Width.HasValue)
                    lines.Add($"    width: {cp.Width.Value}");
                if (cp.Height.HasValue)
                    lines.Add($"    height: {cp.Height.Value}");
                
                if (!string.IsNullOrEmpty(cp.BaselineImagePath))
                    lines.Add($"    baselineImagePath: {cp.BaselineImagePath}");
                
                lines.Add("");
            }

            File.WriteAllLines(filePath, lines);
            Log($"[CheckpointRecorder] Exported {_checkpoints.Count} checkpoints to {filePath}");
        }

        /// <summary>
        /// Imports checkpoints from a YAML file.
        /// </summary>
        public void Import(string filePath)
        {
            // TODO: Implement YAML parsing
            Log($"[CheckpointRecorder] Import not yet implemented for {filePath}");
        }

        /// <summary>
        /// Clears all checkpoints.
        /// </summary>
        public void Clear()
        {
            _checkpoints.Clear();
            Log("[CheckpointRecorder] Cleared all checkpoints");
        }

        private string GetPropertyValue(string elementAlias, string propertyName)
        {
            try
            {
                // Parse element alias to get name
                var parts = elementAlias.Split('.');
                var name = parts.Length > 1 ? parts[^1] : elementAlias;

                return propertyName.ToLower() switch
                {
                    "text" => GetText(name),
                    "isenabled" => GetIsEnabled(name),
                    "isvisible" => GetIsVisible(name),
                    _ => GetAttributeValue(elementAlias, propertyName)
                };
            }
            catch (Exception ex)
            {
                Log($"[CheckpointRecorder] Error getting property: {ex.Message}");
                return "";
            }
        }

        private string GetText(string name)
        {
            var response = _client.Send("GetText", name: name);
            return response.Success ? (response.Data ?? "") : "";
        }

        private string GetIsEnabled(string name)
        {
            var response = _client.Send("IsEnabled", name: name);
            return response.Success ? (response.Data ?? "false") : "false";
        }

        private string GetIsVisible(string name)
        {
            var response = _client.Send("IsVisible", name: name);
            return response.Success ? (response.Data ?? "false") : "false";
        }

        private string GetAttributeValue(string elementAlias, string attributeName)
        {
            // For now, return empty - actual implementation would use GetAttribute command
            return "";
        }

        private string GetDataGridContent(string elementName)
        {
            var response = _client.Send("GetDataGridContent", name: elementName);
            return response.Success ? (response.Data ?? "") : "";
        }

        private string CaptureAreaImage(double x, double y, double width, double height)
        {
            var fileName = $"baseline_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}.png";
            var filePath = Path.Combine(_baselineImagesDir, fileName);

            // Send capture command to agent
            var response = _client.Send("CaptureArea", x: x, y: y, width: width, height: height);
            if (response.Success && !string.IsNullOrEmpty(response.Data))
            {
                try
                {
                    var bytes = Convert.FromBase64String(response.Data);
                    File.WriteAllBytes(filePath, bytes);
                    return filePath;
                }
                catch { }
            }

            return filePath;
        }

        private string GetOcrText(double x, double y, double width, double height)
        {
            // For area checkpoints, we capture and would use OCR
            // This requires pytesseract integration on the Python side
            return $"[OCR would extract text from area ({x},{y},{width},{height})]";
        }

        private void Log(string message)
        {
            RecordingSession.Log(message);
        }
    }
}
