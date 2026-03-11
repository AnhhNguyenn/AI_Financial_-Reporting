using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Extensions
{
    public static class HierarchyExtensions
    {
        public static List<T> OrderByHierarchy<T>(
            this IEnumerable<T> source,
            Func<T, int?> parentIdSelector,
            Func<T, int> idSelector,
            Func<T, decimal> orderingSelector)
        {
            var items = source as List<T> ?? source.ToList();
            if (items.Count == 0) return items;

            var idSet = new HashSet<int>(items.Select(idSelector));
            var childrenLookup = items.ToLookup(parentIdSelector);
            var result = new List<T>(items.Count);

            void AddNodeAndChildren(T node)
            {
                result.Add(node);

                var nodeId = idSelector(node);

                var children = childrenLookup[nodeId];

                foreach (var child in children.OrderBy(orderingSelector))
                {
                    if (idSelector(child) != nodeId)
                    {
                        AddNodeAndChildren(child);
                    }
                }
            }

            var roots = items.Where(item =>
            {
                var pid = parentIdSelector(item);
                var id = idSelector(item);

                if (pid == null) return true;               
                if (pid == 0) return true;                  
                if (pid == id) return true;                 
                if (!idSet.Contains(pid.Value)) return true;

                return false;
            })
            .OrderBy(orderingSelector);

            foreach (var root in roots)
            {
                AddNodeAndChildren(root);
            }

            return result;
        }
    }
}
