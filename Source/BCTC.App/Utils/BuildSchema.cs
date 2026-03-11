using BCTC.DataAccess.Models;

namespace BCTC.App.Utils
{
    public class BuildSchema
    {
        public static object? BuildSchemaFor<T>()
        {
            if (typeof(T) == typeof(RouterResult))
            {
                return new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        tables = new
                        {
                            type = "ARRAY",
                            items = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    type = new { type = "STRING" },
                                    start_page = new { type = "INTEGER" },
                                    end_page = new { type = "INTEGER" }
                                },
                                required = new[] { "type", "start_page", "end_page" }
                            }
                        }
                    },
                    required = new[] { "tables" }
                };
            }

            return null;
        }

    }
}
