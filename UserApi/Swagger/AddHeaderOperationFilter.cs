using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace UserApi.Swagger;

public class AddHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= new List<OpenApiParameter>();

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Login",
            In = ParameterLocation.Header,
            Required = false,
            Schema = new OpenApiSchema { Type = "string" },
            Description = "Логин администратора"
        });

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Password",
            In = ParameterLocation.Header,
            Required = false,
            Schema = new OpenApiSchema { Type = "string" },
            Description = "Пароль администратора"
        });
    }
}
