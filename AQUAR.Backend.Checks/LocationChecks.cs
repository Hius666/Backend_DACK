using AQUAR.Api;
using Microsoft.Extensions.Options;

internal static class LocationChecks
{
    public static void Run()
    {
        void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS GPS: "+name);}
        var time=DateTime.UtcNow;
        // Synthetic unit-test origin only; no production/company coordinate is configured here.
        var service=new AttendanceLocationService(Options.Create(new CompanyLocationOptions{Latitude=0,Longitude=0}));
        var inside=service.Capture(AttendanceEvent.CheckIn,0,0.00045,time);
        Check(inside.Status==LocationStatus.InRange&&inside.DistanceMeters>49&&inside.DistanceMeters<51&&inside.Alert==null,"Within default 100m radius");
        var outside=service.Capture(AttendanceEvent.CheckOut,0,0.002,time);
        Check(outside.Status==LocationStatus.OutOfRange&&outside.DistanceMeters>200&&outside.Alert?.Kind=="OutsideCompanyRadius","Outside radius creates alert without rejection");
        Check(outside.RecordedAtUtc==time&&outside.Alert?.CreatedAtUtc==time,"Location and alert use server event time");
        var absent=service.Capture(AttendanceEvent.CheckIn,null,null,time);
        Check(absent.Status==LocationStatus.Unverified&&absent.DistanceMeters==null&&absent.Alert==null,"No GPS remains unverified, no alert");
        var unconfigured=new AttendanceLocationService(Options.Create(new CompanyLocationOptions())).Capture(AttendanceEvent.CheckIn,0,0,time);
        Check(unconfigured.Status==LocationStatus.Unverified&&unconfigured.VerificationReason=="CompanyNotConfigured","No invented company coordinates");
        foreach(var pair in new (double?,double?)[]{(91,0),(0,181),(double.NaN,0),(double.PositiveInfinity,0),(0,null),(null,0)}){
            try{service.Capture(AttendanceEvent.CheckIn,pair.Item1,pair.Item2,time);throw new Exception("Invalid coordinate accepted");}
            catch(ApiException e){Check(e.Status==400,"Invalid/partial/nonfinite GPS rejected");}
        }
        Check(AttendanceLocationService.DistanceMeters(0,0,0,0)==0,"Coincident points distance zero");
        Check(Math.Abs(AttendanceLocationService.DistanceMeters(0,179.999,0,-179.999)-222.39)<1,"Haversine crosses date line correctly");
        Check(new CompanyLocationOptions().IsValid()&&!new CompanyLocationOptions{Latitude=0}.IsValid()&&!new CompanyLocationOptions{RadiusMeters=0}.IsValid(),"Optional company config validates pairs and radius");
    }
}
