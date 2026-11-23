using Cortex.Mediator.DependencyInjection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SanApp.Application.Validation;

namespace SanApp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var applicationAssembly = typeof(DependencyInjection).Assembly;
        services.AddValidatorsFromAssembly(applicationAssembly);
        services.AddCortexMediator(
            [typeof(DependencyInjection)],
            options => options
                .AddDefaultBehaviors()
                .AddOpenCommandPipelineBehavior(typeof(CommandValidationBehavior<,>))
                .AddOpenQueryPipelineBehavior(typeof(QueryValidationBehavior<,>)));

        return services;
    }
}
