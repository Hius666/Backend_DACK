using Microsoft.Extensions.Options;

namespace AQUAR.Api;

public sealed class CompanyLocationOptions
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double RadiusMeters { get; set; } = 100;
    public bool IsValid() => double.IsFinite(RadiusMeters) && RadiusMeters > 0 &&
        ((Latitude == null && Longitude == null) ||
         (Latitude.HasValue && Longitude.HasValue && AttendanceLocationService.ValidCoordinates(Latitude.Value, Longitude.Value)));
}

public sealed class AttendanceLocationService(IOptions<CompanyLocationOptions> options)
{
    public const double EarthRadiusMeters = 6371008.8;
    public static bool ValidCoordinates(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude) && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        static double Radians(double degrees) => degrees * Math.PI / 180;
        var a = Math.Pow(Math.Sin(Radians(lat2 - lat1) / 2), 2) +
            Math.Cos(Radians(lat1)) * Math.Cos(Radians(lat2)) * Math.Pow(Math.Sin(Radians(lon2 - lon1) / 2), 2);
        return EarthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(Math.Clamp(a, 0, 1)), Math.Sqrt(1 - Math.Clamp(a, 0, 1)));
    }

    public AttendanceLocation Capture(AttendanceEvent eventType, double? latitude, double? longitude, DateTime serverTime)
    {
        if (latitude.HasValue != longitude.HasValue ||
            (latitude.HasValue && !ValidCoordinates(latitude.Value, longitude!.Value)))
            throw new ApiException(400, "GPS cần đủ latitude [-90,90] và longitude [-180,180], là các số hữu hạn.");
        var cfg = options.Value;
        var location = new AttendanceLocation {
            Event = eventType, Latitude = latitude, Longitude = longitude, RecordedAtUtc = serverTime,
            Status = LocationStatus.Unverified, VerificationReason = "NoGps",
            CompanyLatitude = cfg.Latitude, CompanyLongitude = cfg.Longitude, RadiusMeters = cfg.RadiusMeters
        };
        if (!latitude.HasValue) return location;
        if (!cfg.Latitude.HasValue || !cfg.Longitude.HasValue) { location.VerificationReason = "CompanyNotConfigured"; return location; }
        location.DistanceMeters = DistanceMeters(cfg.Latitude.Value, cfg.Longitude.Value, latitude.Value, longitude!.Value);
        location.Status = location.DistanceMeters <= cfg.RadiusMeters ? LocationStatus.InRange : LocationStatus.OutOfRange;
        location.VerificationReason = "ClientCoordinatesCompared";
        if (location.Status == LocationStatus.OutOfRange)
            location.Alert = new AttendanceLocationAlert { CreatedAtUtc = serverTime, Kind = "OutsideCompanyRadius" };
        return location;
    }

    public static object View(AttendanceLocation? location) => new {
        latitude = location?.Latitude, longitude = location?.Longitude, recordedAtUtc = location?.RecordedAtUtc,
        status = (location?.Status ?? LocationStatus.Unverified).ToString(), distanceMeters = location?.DistanceMeters,
        radiusMeters = location?.RadiusMeters, verificationReason = location?.VerificationReason ?? "NoLocationRecord"
    };
}
