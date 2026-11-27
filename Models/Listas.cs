namespace OSO.Models;

public static class Listas
{
    // Ordena la lista dejando en su orden original los elementos que la comparación considera iguales
    // (List.Sort solo no lo garantiza).
    public static void OrdenarEstable<T>(List<T> lista, Comparison<T> comparar) where T : class
    {
        var posiciones = new Dictionary<T, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < lista.Count; i++)
        {
            posiciones[lista[i]] = i;
        }

        lista.Sort((a, b) =>
        {
            int orden = comparar(a, b);
            return orden != 0 ? orden : posiciones[a].CompareTo(posiciones[b]);
        });
    }
}
