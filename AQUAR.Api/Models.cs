using Microsoft.EntityFrameworkCore;
namespace AQUAR.Api;
public enum UserRole { Admin, Manager, Employee }
public class Staff {
 public int Id {get;set;} public string Name {get;set;}=""; public string Email {get;set;}="";
 public string PasswordHash {get;set;}=""; public UserRole Role {get;set;}=UserRole.Employee;
 public bool Active {get;set;}=true; public decimal HourlyRate {get;set;}
 public bool MustChangePassword {get;set;}
 public int TokenVersion {get;set;}
 public int? ManagerId {get;set;} public string? NfcCardId {get;set;} public int? DepartmentId {get;set;}
}
public class Department { public int Id {get;set;} public string Name {get;set;}=""; public string? Description {get;set;} }
public class Attendance {
 public int Id {get;set;} public int StaffId {get;set;} public Staff Staff {get;set;}=null!;
 public DateTime CheckInUtc {get;set;} public DateTime? CheckOutUtc {get;set;}
 public string Method {get;set;}=""; public bool Manual {get;set;} public int? RecordedBy {get;set;} public string? Reason {get;set;}
 public List<AttendanceLocation> Locations {get;set;}=[];
}
public enum AttendanceEvent { CheckIn=1, CheckOut=2 }
public enum LocationStatus { Unverified=0, InRange=1, OutOfRange=2 }
public class AttendanceLocation {
 public int Id {get;set;} public int AttendanceId {get;set;} public Attendance Attendance {get;set;}=null!;
 public AttendanceEvent Event {get;set;} public double? Latitude {get;set;} public double? Longitude {get;set;}
 public DateTime RecordedAtUtc {get;set;} public LocationStatus Status {get;set;}
 public double? DistanceMeters {get;set;} public double? CompanyLatitude {get;set;} public double? CompanyLongitude {get;set;}
 public double RadiusMeters {get;set;} public string VerificationReason {get;set;}="";
 public AttendanceLocationAlert? Alert {get;set;}
}
public class AttendanceLocationAlert {
 public int Id {get;set;} public int AttendanceLocationId {get;set;} public AttendanceLocation Location {get;set;}=null!;
 public DateTime CreatedAtUtc {get;set;} public string Kind {get;set;}="";
}

