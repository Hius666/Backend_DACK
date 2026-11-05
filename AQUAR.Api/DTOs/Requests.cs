using System.ComponentModel.DataAnnotations;
namespace AQUAR.Api;
public record LoginRequest([Required,EmailAddress]string Email,[Required]string Password);
public record StaffRequest([Required,StringLength(200)]string Name,[Required,EmailAddress,StringLength(254)]string Email,[Required,StrongPassword]string Password,[EnumDataType(typeof(UserRole))]UserRole Role,[Range(0,1000000000)]decimal HourlyRate,int? ManagerId,int? DepartmentId=null);
public record StaffUpdate([Required,StringLength(200)]string Name,[Required,EmailAddress,StringLength(254)]string Email,[EnumDataType(typeof(UserRole))]UserRole Role,[Range(0,1000000000)]decimal HourlyRate,int? ManagerId,bool Active,int? DepartmentId=null);
public record StaffStatusRequest([Required]bool? Active);
public record ManualRequest(int StaffId,DateTime CheckInUtc,DateTime CheckOutUtc,[Required,StringLength(2000)]string Reason);
public record CheckRequest([Required]string Method,double? Latitude=null,double? Longitude=null);
public record PasswordRequest([Required]string CurrentPassword,[Required,StrongPassword]string NewPassword,[Required]string ConfirmNewPassword);
public record ResetPasswordRequest([Required,StrongPassword]string NewPassword);
public record DepartmentRequest([Required,StringLength(200)]string Name,[StringLength(2000)]string? Description);

