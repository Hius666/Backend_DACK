using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
namespace AQUAR.Api;
public class TokenService(IConfiguration cfg) {
 public string Create(Staff s) {
 var claims=new[]{new Claim(ClaimTypes.NameIdentifier,s.Id.ToString()),new Claim(ClaimTypes.Role,s.Role.ToString()),new Claim(ClaimTypes.Name,s.Name),new Claim("credential_stamp",PasswordService.Stamp(s.PasswordHash)),new Claim("token_version",s.TokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),new Claim(JwtRegisteredClaimNames.Jti,Guid.NewGuid().ToString())};
 return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(cfg["Jwt:Issuer"],cfg["Jwt:Audience"],claims,expires:DateTime.UtcNow.AddHours(8),signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!)),SecurityAlgorithms.HmacSha256)));
 }
}

