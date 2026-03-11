using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MappingReportNorm.Utils
{
    public static class FullPathBuilder
    {
        /// <summary>
        /// Build FullPath cho cấu trúc cây với tên field động
        /// </summary>
        public static List<T> BuildDynamic<T>(
            List<T> items,
            string idField,
            string parentIdField,
            string nameField,
            string fullPathField,
            string separator = " > ")
        {
            if (items == null || items.Count == 0)
                return items;

            var type = typeof(T);
            var idProp = type.GetProperty(idField);
            var parentIdProp = type.GetProperty(parentIdField);
            var nameProp = type.GetProperty(nameField);
            var fullPathProp = type.GetProperty(fullPathField);

            if (idProp == null || parentIdProp == null || nameProp == null || fullPathProp == null)
                throw new ArgumentException("Invalid field names");

            // Dictionary để lookup nhanh theo ID
            var itemDict = new Dictionary<object, T>();
            foreach (var item in items)
            {
                var id = idProp.GetValue(item);
                if (id != null)
                    itemDict[id] = item;
            }

            // Build FullPath cho từng item
            foreach (var item in items)
            {
                var fullPath = BuildFullPathForItem<T>(
                    item,
                    idProp,
                    parentIdProp,
                    nameProp,
                    fullPathProp,
                    itemDict,
                    separator);

                fullPathProp.SetValue(item, fullPath);
            }

            return items;
        }

        /// <summary>
        /// Build FullPath cho 1 item bằng cách duyệt từ con lên cha
        /// </summary>
        private static string BuildFullPathForItem<T>(
            T item,
            PropertyInfo idProp,
            PropertyInfo parentIdProp,
            PropertyInfo nameProp,
            PropertyInfo fullPathProp,
            Dictionary<object, T> itemDict,
            string separator)
        {
            var pathParts = new List<string>();
            var current = item;
            var visited = new HashSet<object>(); // Tránh vòng lặp vô hạn

            while (current != null)
            {
                var name = nameProp.GetValue(current)?.ToString();
                if (!string.IsNullOrEmpty(name))
                    pathParts.Insert(0, name);

                var currentId = idProp.GetValue(current);
                var parentId = parentIdProp.GetValue(current);

                // Kiểm tra điều kiện node gốc
                if (IsRootNode(currentId, parentId, itemDict))
                    break;

                // Kiểm tra vòng lặp
                if (visited.Contains(parentId))
                    break;

                visited.Add(parentId);

                // Tìm parent
                if (!itemDict.TryGetValue(parentId, out var parent))
                    break;

                current = parent;
            }

            return string.Join(separator, pathParts);
        }

        /// <summary>
        /// Kiểm tra có phải node gốc không
        /// </summary>
        private static bool IsRootNode<T>(object currentId, object parentId, Dictionary<object, T> itemDict)
        {
            // ParentID = null
            if (parentId == null)
                return true;

            // ParentID = 0 (cho int, long, ...)
            if (IsZero(parentId))
                return true;

            // ParentID = chính ID của nó (self-reference)
            if (AreEqual(currentId, parentId))
                return true;

            // ParentID có giá trị nhưng không tồn tại trong list
            if (!itemDict.ContainsKey(parentId))
                return true;

            return false;
        }

        /// <summary>
        /// Kiểm tra 2 object có bằng nhau không
        /// </summary>
        private static bool AreEqual(object obj1, object obj2)
        {
            if (obj1 == null && obj2 == null)
                return true;
            if (obj1 == null || obj2 == null)
                return false;

            return obj1.Equals(obj2);
        }

        /// <summary>
        /// Kiểm tra giá trị = 0
        /// </summary>
        private static bool IsZero(object value)
        {
            if (value == null)
                return false;

            var type = value.GetType();

            if (type == typeof(int))
                return (int)value == 0;
            if (type == typeof(long))
                return (long)value == 0;
            if (type == typeof(decimal))
                return (decimal)value == 0;
            if (type == typeof(double))
                return (double)value == 0;
            if (type == typeof(float))
                return (float)value == 0;
            if (type == typeof(short))
                return (short)value == 0;
            if (type == typeof(byte))
                return (byte)value == 0;

            return false;
        }
    }
}
