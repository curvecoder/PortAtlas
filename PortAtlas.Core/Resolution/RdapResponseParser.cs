using System.Text.Json;

namespace PortAtlas.Core.Resolution;

/// <summary>
/// Parser for RDAP JSON responses (RFC 7483) extracting organization, country, and network block metadata.
/// </summary>
public static class RdapResponseParser
{
    private const int MaxOrganizationLength = 80;

    /// <summary>
    /// Extracts the owning organization or entity name from an RDAP JSON response.
    /// </summary>
    /// <param name="jsonContent">Raw JSON payload returned by the RDAP service.</param>
    /// <returns>Cleaned organization name, or null if not found.</returns>
    public static string? ExtractOrganization(string jsonContent)
    {
        return ExtractOwnerDetails(jsonContent)?.Organization;
    }

    /// <summary>
    /// Extracts complete network block metadata (organization, country, network range, handle) from an RDAP JSON response.
    /// </summary>
    /// <param name="jsonContent">Raw JSON payload returned by the RDAP service.</param>
    /// <returns>Extracted metadata, or null if the payload is empty or invalid.</returns>
    public static IpOwnerMetadata? ExtractOwnerDetails(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                // Valid JSON that is not an RDAP object (an error page body, a bare value) holds no owner.
                return null;
            }

            var organization = ExtractOrganizationFromRoot(root);

            string? country = null;
            if (root.TryGetProperty("country", out var countryProp) && countryProp.ValueKind == JsonValueKind.String)
            {
                country = countryProp.GetString();
            }

            string? networkRange = null;
            if (root.TryGetProperty("startAddress", out var startProp) &&
                root.TryGetProperty("endAddress", out var endProp) &&
                startProp.ValueKind == JsonValueKind.String &&
                endProp.ValueKind == JsonValueKind.String)
            {
                var start = startProp.GetString();
                var end = endProp.GetString();
                if (!string.IsNullOrWhiteSpace(start) && !string.IsNullOrWhiteSpace(end))
                {
                    networkRange = $"{start} - {end}";
                }
            }

            string? handle = null;
            if (root.TryGetProperty("handle", out var handleProp) && handleProp.ValueKind == JsonValueKind.String)
            {
                handle = handleProp.GetString();
            }

            if (organization is null && country is null && networkRange is null && handle is null)
            {
                return null;
            }

            return new IpOwnerMetadata(organization, country, networkRange, handle);
        }
        catch (JsonException)
        {
            // A body truncated by the network or not in JSON carries no usable owner: the caller gets null, as for a
            // response without owner fields.
        }

        return null;
    }

    private static string? ExtractOrganizationFromRoot(JsonElement root)
    {
        // 1. Priority lookup in entities with 'registrant' role
        if (root.TryGetProperty("entities", out var entities) && entities.ValueKind == JsonValueKind.Array)
        {
            var registrantOrg = FindOrganizationInEntities(entities, targetRole: "registrant");
            if (!string.IsNullOrWhiteSpace(registrantOrg))
            {
                return SanitizeName(registrantOrg);
            }

            // 2. Secondary lookup across any other entity role
            var anyOrg = FindOrganizationInEntities(entities, targetRole: null);
            if (!string.IsNullOrWhiteSpace(anyOrg))
            {
                return SanitizeName(anyOrg);
            }
        }

        // 3. Fallback to root 'name' property (e.g. "MSFT", "FR-PROXAD")
        if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
        {
            var nameVal = nameProp.GetString();
            if (!string.IsNullOrWhiteSpace(nameVal))
            {
                return SanitizeName(nameVal);
            }
        }

        return null;
    }

    private static string? FindOrganizationInEntities(JsonElement entities, string? targetRole)
    {
        // Registries do not all follow RFC 7483 to the letter: an element of an unexpected JSON type is skipped rather
        // than read, because JsonElement accessors throw InvalidOperationException on a type mismatch.
        foreach (var entity in entities.EnumerateArray())
        {
            if (entity.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var roleMatches = targetRole is null;
            if (targetRole is not null && entity.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in roles.EnumerateArray())
                {
                    if (role.ValueKind == JsonValueKind.String && role.ValueEquals(targetRole))
                    {
                        roleMatches = true;
                        break;
                    }
                }
            }

            if (!roleMatches)
            {
                continue;
            }

            // Extract from vcardArray: ["vcard", [ [ "version", {}, "text", "4.0" ], [ "fn", {}, "text", "Name" ], ... ]]
            if (entity.TryGetProperty("vcardArray", out var vcardArray) &&
                vcardArray.ValueKind == JsonValueKind.Array &&
                vcardArray.GetArrayLength() >= 2)
            {
                var cardProps = vcardArray[1];
                if (cardProps.ValueKind == JsonValueKind.Array)
                {
                    string? candidateFn = null;
                    string? candidateOrg = null;

                    foreach (var prop in cardProps.EnumerateArray())
                    {
                        if (prop.ValueKind == JsonValueKind.Array && prop.GetArrayLength() >= 4)
                        {
                            // A structured "org" value is a JSON array (RFC 7095): it is skipped and "fn" is used instead.
                            var propName = ReadTextOrNull(prop[0]);
                            if (string.Equals(propName, "org", StringComparison.OrdinalIgnoreCase))
                            {
                                candidateOrg = ReadTextOrNull(prop[3]) ?? candidateOrg;
                            }
                            else if (string.Equals(propName, "fn", StringComparison.OrdinalIgnoreCase))
                            {
                                candidateFn = ReadTextOrNull(prop[3]) ?? candidateFn;
                            }
                        }
                    }

                    var selected = !string.IsNullOrWhiteSpace(candidateOrg) ? candidateOrg : candidateFn;
                    if (!string.IsNullOrWhiteSpace(selected))
                    {
                        return selected;
                    }
                }
            }

            // Recursive lookup in nested entities if present
            if (entity.TryGetProperty("entities", out var nestedEntities) && nestedEntities.ValueKind == JsonValueKind.Array)
            {
                var nestedResult = FindOrganizationInEntities(nestedEntities, targetRole);
                if (!string.IsNullOrWhiteSpace(nestedResult))
                {
                    return nestedResult;
                }
            }
        }

        return null;
    }

    private static string? ReadTextOrNull(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static string SanitizeName(string raw)
    {
        var cleaned = raw.Trim();
        if (cleaned.Length > MaxOrganizationLength)
        {
            cleaned = cleaned[..MaxOrganizationLength].TrimEnd();
        }

        return cleaned;
    }
}
