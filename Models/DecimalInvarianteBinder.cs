using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace OSO.Models;

// Los <input type="number"> mandan siempre el decimal con punto ("0.5"). Con la cultura es-AR del servidor,
// el punto es separador de miles y "0.5" se leería como 5: por eso los decimales se leen con cultura invariante.
public class DecimalInvarianteBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        Type tipo = context.Metadata.ModelType;
        return tipo == typeof(decimal) || tipo == typeof(decimal?) ? new DecimalInvarianteBinder(tipo == typeof(decimal?)) : null;
    }
}

public class DecimalInvarianteBinder(bool aceptaNulo) : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ValueProviderResult valor = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valor == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valor);
        string? texto = valor.FirstValue?.Trim();

        if (string.IsNullOrEmpty(texto))
        {
            if (aceptaNulo)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }

            return Task.CompletedTask;
        }

        // Si vino con coma decimal y sin punto ("0,5"), se acepta igual.
        if (texto.Contains(',') && !texto.Contains('.'))
        {
            texto = texto.Replace(',', '.');
        }

        if (decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal numero))
        {
            bindingContext.Result = ModelBindingResult.Success(numero);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "El número no es válido.");
        }

        return Task.CompletedTask;
    }
}
