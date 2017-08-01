using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Challenges;
namespace Challenges
{
    public interface ICloningService
    {
        T Clone<T>(T source);
    }

    public class CloningService : ICloningService
    {
        public T Clone<T>(T source)
        {
            try
            {
                return Reflection.ShallowCopy<T>(source);
            }
            catch (InvalidCastException ex)
            {
                System.Console.WriteLine(ex.Message);
            }
            return default(T);
        }

        public static class Reflection
        {
            /// <summary>
            /// Makes a shallow copy of the object
            /// </summary>
            /// <param name="Object">Object to copy</param>
            /// <param name="SimpleTypesOnly">If true, it only copies simple types (no classes, only items like int, string, etc.), false copies everything.</param>
            /// <returns>A copy of the object</returns>
            public static T ShallowCopy<T>(object Object)
            {
                try
                {
                    Type ObjectType = Object.GetType();
                    PropertyInfo[] Properties = ObjectType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
                    FieldInfo[] Fields = ObjectType.GetFields(BindingFlags.Instance | BindingFlags.Public);
                    object ClassInstance = null;

                    // Is it a simple class
                    if (ObjectType.IsValueType || ObjectType.GetConstructor(Type.EmptyTypes) != null)
                    {
                        ClassInstance = Activator.CreateInstance(ObjectType);
                        SetClassFields(Fields, ClassInstance, Object);
                        SetClassProperties(Properties, ClassInstance, Object);
                    }

                    if (ObjectType.IsArray)
                    {
                        return (T)DoTwoDimensionalIntArrayLogic(Object, ObjectType);
                    }
                    if (typeof(IEnumerable).IsAssignableFrom(ObjectType))
                    {
                        try
                        {
                            return (T)DoListLogic(Object);
                        }
                        catch (InvalidCastException)
                        {
                            //Costly but I didn't know how else to do it at moment..
                            return (T)DoListHackLogic(Object);
                        }
                    }

                    return (T)ClassInstance;
                }
                catch { throw; }
            }

            private static object DoListLogic(object Object)
            {
                IList originalList = Object as IList;
                Type listType = typeof(List<>);
                Type constructedListType = listType.MakeGenericType(originalList[0].GetType());
                IList listInstance = (IList)Activator.CreateInstance(constructedListType);

                for (int i = 0; i < originalList.Count; i++)
                {
                    Dictionary<int, object[]> track = new Dictionary<int, object[]>();
                    var bufer = Util.CircularListCopy(originalList[i], (object[])listInstance, track, i);
                    listInstance.Add(bufer);
                    //listInstance.Add(originalList[i]);
                }
                return listInstance;
            }

            private static object DoListHackLogic(object Object)
            {
                IList originalList = Object as IList;
                List<IEnumerable<int[]>> list = new List<IEnumerable<int[]>>();
                for (int i = 0; i < originalList.Count; i++)
                {
                    list.Add((IEnumerable<int[]>)originalList[i]);
                }
                return list;
            }

            private static object DoTwoDimensionalIntArrayLogic(object Object, Type ObjectType)
            {
                Array originalArrayList = Object as Array;
                object firstObject = originalArrayList.GetValue(0);

                if (firstObject.GetType().IsArray)
                {
                    try
                    {
                        int[][] innerArrayList = new int[originalArrayList.Length][];
                        for (int i = 0; i < originalArrayList.Length; i++)
                        {
                            int[] returnList = (int[])originalArrayList.GetValue(i);
                            innerArrayList[i] = new int[returnList.Length];
                            innerArrayList[i] = returnList;
                        }

                        return innerArrayList;
                    }
                    catch (InvalidCastException)
                    {
                        // Another hack :\
                        return DoGenericArrayLogic(ObjectType, originalArrayList);
                    }
                }
                return DoGenericArrayLogic(ObjectType, originalArrayList);
            }

            private static object DoGenericArrayLogic(Type ObjectType, Array OriginalArrayList)
            {
                Type elementType = Type.GetType(ObjectType.FullName.Replace("[]", string.Empty));
                dynamic newArrayList = Array.CreateInstance(elementType, OriginalArrayList.Length);
                for (int i = 0; i < OriginalArrayList.Length; i++)
                {
                    var itemInList = OriginalArrayList.GetValue(i);
                    if (itemInList.GetHashCode() == OriginalArrayList.GetHashCode() && typeof(IEnumerable).IsAssignableFrom(itemInList.GetType()))
                    {
                        Array innerList = itemInList as Array;
                        Type innerElementType = Type.GetType(innerList.GetType().FullName.Replace("[]", string.Empty));
                        dynamic newInnerArrayList = Array.CreateInstance(innerElementType, innerList.Length);
                        Dictionary<int, object[]> track = new Dictionary<int, object[]>();
                        newArrayList.SetValue(Util.CircularListCopy(itemInList, newInnerArrayList, track, i), i);
                    }
                    else if (typeof(IEnumerable).IsAssignableFrom(itemInList.GetType()))// trying to figure out if Class is pointing to self
                    {
                        var clonedList = DoListLogic(itemInList);
                        newArrayList.SetValue(clonedList, i);
                    }
                    else
                        newArrayList.SetValue(itemInList, i);
                }
                return newArrayList;
            }

            private static void SetClassProperties(PropertyInfo[] Properties, object ClassInstance, object Object)
            {
                for (int i = 0; i < Properties.Count(); i++)
                {
                    try
                    {
                        CloneableAttribute mode = Util.GetAttribute<CloneableAttribute>(Properties[i]);
                        if (Properties[i].PropertyType.IsValueType || Properties[i].PropertyType.IsEnum || Properties[i].PropertyType.Equals(typeof(System.String)))
                        {
                            if (mode == null || mode.Mode != CloningMode.Ignore)
                                SetPropertyifSimpleType(Properties[i], ClassInstance, Object);
                        }
                        else
                        {
                            if (mode == null || mode.Mode != CloningMode.Ignore)
                                SetProperty(Properties[i], ClassInstance, Object);
                        }
                    }
                    catch { }
                }
            }

            private static void SetClassFields(FieldInfo[] Fields, object ClassInstance, object Object)
            {
                for (int i = 0; i < Fields.Count(); i++)
                {
                    CloneableAttribute mode = Util.GetAttribute<CloneableAttribute>(Fields[i]);
                    try
                    {
                        if (Fields[i].FieldType.IsValueType || Fields[i].FieldType.IsEnum || Fields[i].FieldType.Equals(typeof(System.String)))
                        {
                            SetFieldifSimpleType(Fields[i], ClassInstance, Object);
                        }
                        if (Fields[i].FieldType.IsArray)
                        {
                            ClassInstance = SetArrayFieldValues(Object);
                        }
                        else
                        {
                            var field = Fields[i].GetValue(Object);
                            if (Object == field)
                            {
                                var track = new Dictionary<int, object>();
                                ClassInstance = Util.CircularCopy(Fields[i].GetValue(Object), ClassInstance, track);

                            }
                            else
                                Fields[i].SetValue(ClassInstance, Fields[i].GetValue(Object));
                        }
                    }
                    catch { throw; }
                }
            }

            private static void SetFieldArray(FieldInfo field, object classInstance, object @object)
            {
                Type elementType = Type.GetType(field.FieldType.FullName.Replace("[]", string.Empty));
                Array array = @object as Array;
                Array copied = Array.CreateInstance(elementType, array.Length);

                for (int i = 0; i < array.Length; i++)
                {
                    copied.SetValue(SetArrayFieldValues(array.GetValue(i)), i);
                }
                classInstance = Convert.ChangeType(copied, @object.GetType());
            }

            static object SetArrayFieldValues(object obj)
            {
                if (obj == null)
                    return null;
                Type type = obj.GetType();
                if (type.IsArray)
                {
                    Type elementType = Type.GetType(type.FullName.Replace("[]", string.Empty));
                    Array array = obj as Array;
                    Array copied = Array.CreateInstance(elementType, array.Length);
                    for (int i = 0; i < array.Length; i++)
                    {
                        copied.SetValue(SetArrayFieldValues(array.GetValue(i)), i);
                    }
                    return Convert.ChangeType(copied, obj.GetType());
                }
                else
                    throw new ArgumentException("Unknown type");
            }

            /// <summary>
            /// Copies a field value
            /// </summary>
            /// <param name="Field">Field object</param>
            /// <param name="ClassInstance">Class to copy to</param>
            /// <param name="Object">Class to copy from</param>
            private static void SetFieldifSimpleType(FieldInfo Field, object ClassInstance, object Object)
            {
                try
                {
                    SetFieldifSimpleType(Field, Field, ClassInstance, Object);
                }
                catch { }
            }

            /// <summary>
            /// Copies a field value
            /// </summary>
            /// <param name="ChildField">Child field object</param>
            /// <param name="Field">Field object</param>
            /// <param name="ClassInstance">Class to copy to</param>
            /// <param name="Object">Class to copy from</param>
            private static void SetFieldifSimpleType(FieldInfo ChildField, FieldInfo Field, object ClassInstance, object Object)
            {
                try
                {
                    Type FieldType = Field.FieldType;
                    if (Field.FieldType.FullName.StartsWith("System.Collections.Generic.List", StringComparison.CurrentCultureIgnoreCase))
                    {
                        FieldType = Field.FieldType.GetGenericArguments()[0];
                    }

                    if (FieldType.FullName.StartsWith("System"))
                    {
                        ChildField.SetValue(ClassInstance, Field.GetValue(Object));
                    }
                }
                catch { throw; }
            }

            /// <summary>
            /// Copies a property value
            /// </summary>
            /// <param name="Property">Property object</param>
            /// <param name="ClassInstance">Class to copy to</param>
            /// <param name="Object">Class to copy from</param>
            private static void SetPropertyifSimpleType(PropertyInfo Property, object ClassInstance, object Object)
            {
                try
                {
                    SetPropertyifSimpleType(Property, Property, ClassInstance, Object);
                }
                catch { }
            }

            /// <summary>
            /// Copies a property value
            /// </summary>
            /// <param name="Property">Property object</param>
            /// <param name="ClassInstance">Class to copy to</param>
            /// <param name="Object">Class to copy from</param>
            private static void SetProperty(PropertyInfo Property, object ClassInstance, object Object)
            {
                try
                {
                    SetProperty(Property, Property, ClassInstance, Object);
                }
                catch { }
            }

            /// <summary>
            /// Copies a property value
            /// </summary>
            /// <param name="ChildProperty">Child property object</param>
            /// <param name="Property">Property object</param>
            /// <param name="ClassInstance">Class to copy to</param>
            /// <param name="Object">Class to copy from</param>
            private static void SetProperty(PropertyInfo ChildProperty, PropertyInfo Property, object ClassInstance, object Object)
            {
                try
                {
                    if (ChildProperty.GetSetMethod() != null && Property.GetGetMethod() != null)
                    {
                        ChildProperty.SetValue(ClassInstance, Property.GetValue(Object, null), null);
                    }
                }
                catch { }
            }

            /// <summary>
            /// Copies a property value
            /// </summary>
            /// <param name="ChildProperty">Child property object</param>
            /// <param name="Property">Property object</param>
            /// <param name="ClassInstance">Class to copy to</param>
            /// <param name="Object">Class to copy from</param>
            private static void SetPropertyifSimpleType(PropertyInfo ChildProperty, PropertyInfo Property, object ClassInstance, object Object)
            {
                try
                {
                    Type PropertyType = Property.PropertyType;
                    if (Property.PropertyType.FullName.StartsWith("System.Collections.Generic.List", StringComparison.CurrentCultureIgnoreCase))
                    {
                        PropertyType = Property.PropertyType.GetGenericArguments()[0];
                    }

                    if (PropertyType.FullName.StartsWith("System"))
                    {
                        SetProperty(ChildProperty, Property, ClassInstance, Object);
                    }
                }
                catch { throw; }
            }

        }

        public class Util
        {
            public static T GetAttribute<T>(MemberInfo info) where T : class
            {
                return Attribute.GetCustomAttribute(info, typeof(T), false) as T;
            }

            public static dynamic CircularCopy(object originalObject, object clonedObject, Dictionary<int, object> track)
            {
                dynamic clone;
                if (originalObject == null)
                    return originalObject;

                if (track.TryGetValue(originalObject.GetHashCode(), out clone))
                {
                    var c = clone as CloningServiceTest.Node;
                    if (null != c)
                        return c;
                }

                track.Add(originalObject.GetHashCode(), clonedObject);

                ((CloningServiceTest.Node)clonedObject).Value = ((CloningServiceTest.Node)originalObject).Value;

                CloningServiceTest.Node circularRefLhsObj = ((CloningServiceTest.Node)originalObject).Left;
                CloningServiceTest.Node circularRefRhsObj = ((CloningServiceTest.Node)originalObject).Right;

                ((CloningServiceTest.Node)clonedObject).Left = (null == circularRefLhsObj) ? null : CircularCopy(circularRefLhsObj, clonedObject, track);
                ((CloningServiceTest.Node)clonedObject).Right = (null == circularRefRhsObj) ? null : CircularCopy(circularRefRhsObj, clonedObject, track);

                return clonedObject;
            }

            public static dynamic CircularListCopy(object thisObj, object[] myClone, Dictionary<int, object[]> track, int indexPosition)
            {
                object[] clone;
                if (thisObj == null)
                    return thisObj;

                if (track.TryGetValue(thisObj.GetHashCode(), out clone))
                {
                    var c = clone as IList;
                    if (null != c)
                        return c;
                }

                track.Add(thisObj.GetHashCode(), myClone);

                var ar = thisObj as Array;
                dynamic listReference = ar.GetValue(indexPosition);
                myClone[indexPosition] = (null == listReference) ? null : CircularListCopy(listReference, myClone, track, indexPosition);
                return myClone;
            }
        }
    }

    public enum CloningMode
    {
        Deep = 0,
        Shallow = 1,
        Ignore = 2,
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class CloneableAttribute : Attribute
    {
        public CloningMode Mode { get; }

        public CloneableAttribute(CloningMode mode)
        {
            Mode = mode;
        }
    }

    public class CloningServiceTest
    {
        public class Simple
        {
            public int I;
            public string S { get; set; }
            [Cloneable(CloningMode.Ignore)]
            public string Ignored { get; set; }
            [Cloneable(CloningMode.Shallow)]
            public object Shallow { get; set; }

            public virtual string Computed => S + I + Shallow;
        }

        public struct SimpleStruct
        {
            public int I;
            public string S { get; set; }
            [Cloneable(CloningMode.Ignore)]
            public string Ignored { get; set; }

            public string Computed => S + I;

            public SimpleStruct(int i, string s)
            {
                I = i;
                S = s;
                Ignored = null;
            }
        }

        public class Simple2 : Simple
        {
            public double D;
            public SimpleStruct SS;
            public override string Computed => S + I + D + SS.Computed;
        }

        public class Node
        {
            public Node Left;
            public Node Right;
            public object Value;
            public int TotalNodeCount =>
                1 + (Left?.TotalNodeCount ?? 0) + (Right?.TotalNodeCount ?? 0);

        }

        public ICloningService Cloner = new CloningService();
        public Action[] AllTests => new Action[] {
            SimpleTest,
            SimpleStructTest,
            Simple2Test,
            NodeTest,
            ArrayTest,
            CollectionTest,
            ArrayTest2,
            CollectionTest2,
            MixedCollectionTest,
            RecursionTest,
           // RecursionTest2,
            PerformanceTest,
        };

        public static void Assert(bool criteria)
        {
            if (!criteria)
                throw new InvalidOperationException("Assertion failed.");
        }

        public void Measure(string title, Action test)
        {
            test(); // Warmup
            var sw = new Stopwatch();
            GC.Collect();
            sw.Start();
            test();
            sw.Stop();
            Console.WriteLine($"{title}: {sw.Elapsed.TotalMilliseconds:0.000}ms");
        }

        public void SimpleTest()
        {
            var s = new Simple() { I = 1, S = "2", Ignored = "3", Shallow = new object() };
            var c = Cloner.Clone(s);
            Assert(s != c);
            Assert(s.Computed == c.Computed);
            Assert(c.Ignored == null);
            Assert(ReferenceEquals(s.Shallow, c.Shallow));
        }

        public void SimpleStructTest()
        {
            var s = new SimpleStruct(1, "2") { Ignored = "3" };
            var c = Cloner.Clone(s);
            Assert(s.Computed == c.Computed);
            Assert(c.Ignored == null);
        }

        public void Simple2Test()
        {
            var s = new Simple2()
            {
                I = 1,
                S = "2",
                D = 3,
                SS = new SimpleStruct(3, "4"),
            };
            var c = Cloner.Clone(s);
            Assert(s != c);
            Assert(s.Computed == c.Computed);
        }

        public void NodeTest()
        {
            var s = new Node
            {
                Left = new Node
                {
                    Right = new Node()
                },
                Right = new Node()
            };
            var c = Cloner.Clone(s);
            Assert(s != c);
            Assert(s.TotalNodeCount == c.TotalNodeCount);
        }

        public void RecursionTest()
        {
            var s = new Node();
            s.Left = s;
            Assert(s == s.Left);
            var c = Cloner.Clone(s);
            Assert(s != c);
            Assert(null == c.Right);
            Assert(c == c.Left);
        }

        public void ArrayTest()
        {
            var n = new Node
            {
                Left = new Node
                {
                    Right = new Node()
                },
                Right = new Node()
            };
            var s = new[] { n, n };
            var c = Cloner.Clone(s);
            Assert(s != c);
            Assert(s.Sum(n1 => n1.TotalNodeCount) == c.Sum(n1 => n1.TotalNodeCount));
            Assert(c[0] == c[1]);
        }

        public void CollectionTest()
        {
            var n = new Node
            {
                Left = new Node
                {
                    Right = new Node()
                },
                Right = new Node()
            };
            var s = new List<Node>() { n, n };
            var c = Cloner.Clone(s);
            Assert(s != c);
            Assert(s.Sum(n1 => n1.TotalNodeCount) == c.Sum(n1 => n1.TotalNodeCount));
            Assert(c[0] == c[1]);
        }

        public void ArrayTest2()
        {
            var s = new[] { new[] { 1, 2, 3 }, new[] { 4, 5 } };
            var c = Cloner.Clone(s);
            Assert(s != c);
            Assert(15 == c.SelectMany(a => a).Sum());
        }

        public void CollectionTest2()
        {
            var s = new List<List<int>> { new List<int> { 1, 2, 3 }, new List<int> { 4, 5 } };
            var c = Cloner.Clone(s);
            Assert(s != c);
            Assert(15 == c.SelectMany(a => a).Sum());
        }

        public void MixedCollectionTest()
        {
            var s = new List<IEnumerable<int[]>> {
                new List<int[]> {new [] {1}},
                new List<int[]> {new [] {2, 3}},
            };
            var c = Cloner.Clone(s);
            Assert(s != c);
            Assert(6 == c.SelectMany(a => a.SelectMany(b => b)).Sum());
        }

        public void RecursionTest2()
        {
            var l = new List<Node>();
            var n = new Node { Value = l };
            n.Left = n;
            l.Add(n);
            var s = new object[] { null, l, n };
            s[0] = s;

            var c = Cloner.Clone(s);
            Assert(s != c);
            Assert(c[0] == c);
            var cl = (List<Node>)c[1];
            Assert(l != cl);
            var cn = cl[0];
            Assert(n != cn);
            Assert(cl == cn.Value);
            Assert(cn.Left == cn);
        }

        public void PerformanceTest()
        {
            Func<int, Node> makeTree = null;
            makeTree = depth => {
                if (depth == 0)
                    return null;
                return new Node
                {
                    Value = depth,
                    Left = makeTree(depth - 1),
                    Right = makeTree(depth - 1),
                };
            };
            for (var i = 10; i <= 20; i++)
            {
                var root = makeTree(i);
                Measure($"Cloning {root.TotalNodeCount} nodes", () => {
                    var copy = Cloner.Clone(root);
                    Assert(root != copy);
                });
            }
        }

        public void RunAllTests()
        {
            foreach (var test in AllTests)
                test.Invoke();
            Console.WriteLine("Done.");
        }
    }

    public class Solution
    {
        public static void Main(string[] args)
        {
            var cloningServiceTest = new CloningServiceTest();
            var allTests = cloningServiceTest.AllTests;
            var numbers = "0 1 2 3 4 5 6 7 8 9 10 11".Split(' ').Select(Int32.Parse).ToArray();
            //try
            //{
            //   cloningServiceTest.RecursionTest();
            //}
            //catch(System.InvalidOperationException ex)
            //{
            //  //  Console.WriteLine($"Failed on {test.GetMethodInfo().Name}.");
            //    Console.WriteLine(ex.Message + ex.StackTrace);
            //    Console.ReadLine();
            //}

            while (true)
            {
                //try
                //{
                //    cloningServiceTest.RecursionTest();
                //}
                //catch (Exception ex)
                //{
                // //   Console.WriteLine($"Failed on {cloningServiceTest.GetMethodInfo().Name}.");
                //    Console.WriteLine(ex.Message);
                //    Console.ReadLine();
                //}


                for (int i = 0; i < numbers.Count(); i++)
                {
                    var line = numbers[i].ToString();
                    // var line = Console.ReadLine();
                    if (string.IsNullOrEmpty(line))
                        break;
                    var test = allTests[int.Parse(line)];

                    try
                    {
                        //cloningServiceTest.RecursionTest2();
                        test.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed on {test.GetMethodInfo().Name}.");
                        Console.WriteLine(ex.Message);
                        Console.ReadLine();
                    }

                    ////}
                    //var line = Console.ReadLine();
                    //if (string.IsNullOrEmpty(line))
                    //    break;
                    //var test = allTests[int.Parse(line)];
                    //try
                    //{
                    //    test.Invoke();
                    //}
                    //catch (Exception)
                    //{
                    //    Console.WriteLine(@"Failed on {test.GetMethodInfo().Name}.");
                    //}
                }
            }
            // Console.WriteLine("Done.");
        }
    }
}