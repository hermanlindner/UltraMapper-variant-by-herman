﻿using System.Collections.Generic;
using System.Linq.Expressions;
using UltraMapper.Configuration;
using UltraMapper.Mappers;
using UltraMapper.MappingConventions;

namespace UltraMapper
{
    public interface IMappingOptions
    {
        CollectionMappingStrategies CollectionMappingStrategy { get; set; }
        ReferenceMappingStrategies ReferenceMappingStrategy { get; set; }
    }

    public interface ITypeOptions : IMappingOptions
    {
        bool IgnoreMemberMappingResolvedByConvention { get; }
    }

    public class GlobalConfiguration
    {
        public readonly TypeConfigurator Configuration;

        /// <summary>
        /// If set to True only explicitly user-defined member-mappings are 
        /// taken into account in the mapping process.
        /// 
        /// If set to False members-mappings that have been resolved by convention 
        /// are taken into account in the mapping process.
        /// </summary>
        public bool IgnoreMemberMappingResolvedByConvention { get; set; }

        public CollectionMappingStrategies CollectionMappingStrategy { get; set; }
        public ReferenceMappingStrategies ReferenceMappingStrategy { get; set; }

        public IMappingConvention MappingConvention { get; set; }
        public HashSet<IMapperExpressionBuilder> Mappers { get; private set; }

        public GlobalConfiguration( TypeConfigurator configuration )
        {
            this.Configuration = configuration;

            this.ReferenceMappingStrategy = ReferenceMappingStrategies.CREATE_NEW_INSTANCE;
            this.CollectionMappingStrategy = CollectionMappingStrategies.RESET;

            this.Mappers = new HashSet<IMapperExpressionBuilder>()
            {
                //Order is important: the first mapper that can handle a mapping is used.
                //Make sure to use collection which preserve insertion order!
                new BuiltInTypeMapper( configuration ),
                new NullableMapper( configuration ),
                new ConvertMapper( configuration ),
                new StructMapper( configuration ),
                new DictionaryMapper( configuration ),
                //new SetMapper( configuration ),
                new StackMapper( configuration ),
                new QueueMapper( configuration ),
                new LinkedListMapper( configuration ),
                new CollectionMapper( configuration ),
                new ReferenceMapper( configuration ),
            };
        }
    }

    public enum ReferenceMappingStrategies { CREATE_NEW_INSTANCE, USE_TARGET_INSTANCE_IF_NOT_NULL }
}