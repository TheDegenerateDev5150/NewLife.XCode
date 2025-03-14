﻿using System.ComponentModel;
using NewLife;
using XCode.Configuration;

namespace XCode;

public partial class EntityBase : ICustomTypeDescriptor/*, IEditableObject*/, INotifyPropertyChanging, INotifyPropertyChanged
{
    #region INotifyPropertyChanged接口
    /// <summary>属性将要改变时</summary>
    public event PropertyChangingEventHandler? PropertyChanging;

    /// <summary>属性改变事件</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>属性改变。重载时记得调用基类的该方法，以设置脏数据属性，否则数据将无法Update到数据库。</summary>
    /// <param name="fieldName">字段名</param>
    /// <param name="newValue">新属性值</param>
    /// <returns>是否允许改变</returns>
    protected virtual Boolean OnPropertyChanging(String fieldName, Object? newValue)
    {
        PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(fieldName));

        // 如果数据没有改变，不应该影响脏数据
        if (IsFromDatabase && CheckEqual(this[fieldName], newValue)) return false;
        //if (CheckEqual(this[fieldName], newValue)) return false;

        Dirtys.Add(fieldName, this[fieldName]);
        //Dirtys[fieldName] = true;
        //OnPropertyChanged(fieldName);

        return true;
    }

    /// <summary>检查相等，主要特殊处理时间相等</summary>
    /// <param name="v1"></param>
    /// <param name="v2"></param>
    /// <returns></returns>
    internal static Boolean CheckEqual(Object? v1, Object? v2)
    {
        if (v1 == null && v2 == null) return true;

        // 字符串允许空值和空串相等，优先对比
        if (v1 is String s1 && s1 == "" && v2 == null) return true;
        if (v2 is String s2 && s2 == "" && v1 == null) return true;

        if (v1 == null || v2 == null) return Equals(v1, v2);

        if (v1.GetType().IsArray || v2.GetType().IsArray)
        {
            //如果两个对象是同一个引用，逐个对比就没意义。直接返回 true
            if (Equals(v1, v2)) return true;
            // 如果两个对象都是数组
            if (v1 is Array a1 && v2 is Array a2)
            {
                // 如果数组长度不同，直接返回 false
                if (a1.Length != a2.Length) return false;

                // 逐个比较数组元素
                for (var i = 0; i < a1.Length; i++)
                {
                    if (!CheckEqual(a1.GetValue(i), a2.GetValue(i))) return false;
                }

                // 所有元素都相等，返回 true
                return true;
            }
            else
            {
                // 如果只有一个是数组，返回 false
                return false;
            }
        }

        switch (Type.GetTypeCode(v1.GetType()))
        {
            case TypeCode.DateTime:
                // 时间存储包括年月日时分秒，后面还有微秒，而我们数据库存储默认不需要微秒，所以时间的相等判断需要做特殊处理
                return v1.ToDateTime().Trim() == v2.ToDateTime().Trim();
            case TypeCode.Int16:
            case TypeCode.Int32:
            case TypeCode.Int64:
            case TypeCode.UInt16:
            case TypeCode.UInt32:
            case TypeCode.UInt64:
                return Convert.ToInt64(v1) == Convert.ToInt64(v2);
            case TypeCode.String:
                return v1 + "" == v2 + "";
            case TypeCode.Single:
            case TypeCode.Double:
                return Math.Abs(v1.ToDouble() - v2.ToDouble()) < 0.000_001;
            case TypeCode.Decimal:
                return Math.Abs((Decimal)v1 - Convert.ToDecimal(v2)) < 0.000_000_000_001m;
            default:
                break;
        }

        return Equals(v1, v2);
    }

    /// <summary>属性改变。重载时记得调用基类的该方法，以设置脏数据属性，否则数据将无法Update到数据库。</summary>
    /// <param name="fieldName">字段名</param>
    protected virtual void OnPropertyChanged(String fieldName)
    {
        //Dirtys[fieldName] = true;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(fieldName));
    }
    #endregion

    #region ICustomTypeDescriptor 成员
    AttributeCollection ICustomTypeDescriptor.GetAttributes()
    {
        // 重载。从DescriptionAttribute和BindColumnAttribute中获取备注，创建DisplayNameAttribute特性
        var atts = TypeDescriptor.GetAttributes(this, true);

        if (!ContainAttribute(atts, typeof(DisplayNameAttribute)))
        {
            var list = new List<Attribute>();
            String? description = null;
            foreach (Attribute item in atts)
            {
                if (item is DescriptionAttribute des)
                {
                    description = des.Description;
                    if (!String.IsNullOrEmpty(description)) break;
                }
                if (item is BindColumnAttribute bca)
                {
                    description = bca.Description;
                    if (!String.IsNullOrEmpty(description)) break;
                }
            }

            if (!String.IsNullOrEmpty(description))
            {
                list.Add(new DisplayNameAttribute(description));
                atts = new AttributeCollection(list.ToArray());
            }
        }

        return atts;
    }

    String ICustomTypeDescriptor.GetClassName() => GetType().FullName;

    String ICustomTypeDescriptor.GetComponentName() => TypeDescriptor.GetComponentName(this, true);

    TypeConverter ICustomTypeDescriptor.GetConverter() => TypeDescriptor.GetConverter(this, true);

    EventDescriptor ICustomTypeDescriptor.GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(this, true);

    PropertyDescriptor ICustomTypeDescriptor.GetDefaultProperty() => TypeDescriptor.GetDefaultProperty(this, true);

    Object ICustomTypeDescriptor.GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(this, editorBaseType, true);

    EventDescriptorCollection ICustomTypeDescriptor.GetEvents(Attribute[] attributes) => TypeDescriptor.GetEvents(this, attributes, true);

    EventDescriptorCollection ICustomTypeDescriptor.GetEvents() => TypeDescriptor.GetEvents(this, true);

    PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties(Attribute[] attributes)
    {
        return Fix(GetType(), TypeDescriptor.GetProperties(this, attributes, true));
    }

    PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties()
    {
        return Fix(GetType(), TypeDescriptor.GetProperties(this, true));
    }

    Object ICustomTypeDescriptor.GetPropertyOwner(PropertyDescriptor pd) => this;

    internal static PropertyDescriptorCollection Fix(Type type, PropertyDescriptorCollection pdc)
    {
        if (pdc.Count <= 0) return pdc;

        var factory = type.AsFactory();

        // 准备字段集合
        var dic = new Dictionary<String, FieldItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in factory.Fields)
        {
            dic.Add(item.Name, item);
        }

        var hasChanged = false;
        var list = new List<PropertyDescriptor>();
        foreach (PropertyDescriptor item in pdc)
        {
            // 显示名与属性名相同，并且没有DisplayName特性
            if (item.Name == item.DisplayName && !ContainAttribute(item.Attributes, typeof(DisplayNameAttribute)))
            {
                // 添加一个特性
                if (dic.TryGetValue(item.Name, out var fi) && !String.IsNullOrEmpty(fi.Description))
                {
                    var dis = new DisplayNameAttribute(fi.Description);
                    list.Add(TypeDescriptor.CreateProperty(type, item, dis));
                    hasChanged = true;
                    continue;
                }
            }
            list.Add(item);
        }
        if (hasChanged) pdc = new PropertyDescriptorCollection(list.ToArray());

        return pdc;
    }

    static Boolean ContainAttribute(AttributeCollection attributes, Type type)
    {
        if (attributes == null || attributes.Count <= 0 || type == null) return false;

        foreach (Attribute item in attributes)
        {
            if (type.IsAssignableFrom(item.GetType())) return true;
        }
        return false;
    }
    #endregion

    #region IEditableObject 成员
    //[NonSerialized]
    //private EntityBase _bak;

    //void IEditableObject.BeginEdit()
    //{
    //    _bak = Clone() as EntityBase;
    //}

    //void IEditableObject.CancelEdit()
    //{
    //    CopyFrom(_bak, false);

    //    _bak = null;
    //}

    //void IEditableObject.EndEdit()
    //{
    //    //Update();
    //    Save();

    //    _bak = null;
    //}
    #endregion
}