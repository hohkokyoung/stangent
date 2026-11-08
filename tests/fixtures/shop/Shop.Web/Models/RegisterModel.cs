using System.ComponentModel.DataAnnotations;

namespace Shop.Web.Models;

public class RegisterModel
{
    // N4: the enum argument shows by member name (EmailAddress), not its number.
    [DataType(DataType.EmailAddress)]
    public string Email { get; set; } = "";

    public string Password { get; set; } = "";
}
