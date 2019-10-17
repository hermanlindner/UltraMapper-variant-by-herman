﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UltraMapper.MappingExpressionBuilders;

namespace UltraMapper.Internals
{
    public sealed class TypeMapping : ITypeOptions, IMapping, IInheritanceOptions
    {
        //Each source and target member is instantiated only once per typeMapping
        //so we can handle their options/configuration override correctly.
        private readonly Dictionary<MemberInfo, MappingSource> _sourceProperties
            = new Dictionary<MemberInfo, MappingSource>();

        private readonly Dictionary<MemberInfo, MappingTarget> _targetProperties
            = new Dictionary<MemberInfo, MappingTarget>();

        /*
         *A source member can be mapped to multiple target members.
         *
         *A target member can be mapped just once and for that reason 
         *multiple mappings override each other and the last one is used.
         *
         *The target member can be therefore used as the key of this dictionary
         */
        public readonly Dictionary<MappingTarget, MemberMapping> MemberMappings;
        public readonly Configuration GlobalConfiguration;
        public TypePair TypePair { get; private set; }

        public MappingResolution MappingResolution { get; internal set; }

        public TypeMapping( Configuration globalConfig, TypePair typePair )
        {
            this.GlobalConfiguration = globalConfig;
            this.TypePair = typePair;
            this.MemberMappings = new Dictionary<MappingTarget, MemberMapping>();
        }

        public LambdaExpression CustomConverter { get; set; }
        public LambdaExpression CustomTargetConstructor { get; set; }

        private bool? _ignoreMappingResolvedByConvention;
        public bool IgnoreMemberMappingResolvedByConvention
        {
            get
            {
                if( _ignoreMappingResolvedByConvention == null )
                {
                    var parent = GlobalConfiguration.GetParentConfiguration( this );
                    if( parent != null ) return parent.IgnoreMemberMappingResolvedByConvention;

                    return GlobalConfiguration.IgnoreMemberMappingResolvedByConvention;
                }

                return _ignoreMappingResolvedByConvention.Value;
            }

            set { _ignoreMappingResolvedByConvention = value; }
        }

        private ReferenceBehaviors? _referenceMappingStrategy = null;
        public ReferenceBehaviors ReferenceBehavior
        {
            get
            {
                if( _referenceMappingStrategy == null )
                {
                    var parent = GlobalConfiguration.GetParentConfiguration( this );
                    if( parent != null ) return parent.ReferenceBehavior;

                    return GlobalConfiguration.ReferenceBehavior;
                }

                return _referenceMappingStrategy.Value;
            }

            set { _referenceMappingStrategy = value; }
        }

        public CollectionBehaviors CollectionBehavior { get; set; }

        private LambdaExpression _collectionItemEqualityComparer = null;
        public LambdaExpression CollectionItemEqualityComparer
        {
            get
            {
                if( _collectionItemEqualityComparer == null )
                {
                    var parent = GlobalConfiguration.GetParentConfiguration( this );
                    if( parent != null ) return parent.CollectionItemEqualityComparer;
                }

                return _collectionItemEqualityComparer;
            }

            set { _collectionItemEqualityComparer = value; }
        }

        private IMappingExpressionBuilder _mapper;
        public IMappingExpressionBuilder Mapper
        {
            get
            {
                if( _mapper == null )
                {
                    _mapper = GlobalConfiguration.Mappers.FirstOrDefault(
                        mapper => mapper.CanHandle( this.TypePair.SourceType, this.TypePair.TargetType ) );

                    if( _mapper == null )
                        throw new Exception( $"No object mapper can handle {this}" );
                }

                return _mapper;
            }
        }

        private LambdaExpression _mappingExpression;
        public LambdaExpression MappingExpression
        {
            get
            {
                if( this.CustomConverter != null )
                    return this.CustomConverter;

                //if( _mappingExpression != null ) return _mappingExpression;

                return _mappingExpression = this.Mapper.GetMappingExpression(
                    this.TypePair.SourceType, this.TypePair.TargetType, this );
            }
        }

        private Action<ReferenceTracking, object, object> _mappingFunc;
        public Action<ReferenceTracking, object, object> MappingFunc
        {
            get
            {
                if( _mappingFunc != null ) return _mappingFunc;

                var referenceTrack = Expression.Parameter( typeof( ReferenceTracking ), "referenceTracker" );
                var sourceLambdaArg = Expression.Parameter( typeof( object ), "sourceInstance" );
                var targetLambdaArg = Expression.Parameter( typeof( object ), "targetInstance" );

                var sourceType = TypePair.SourceType;
                var targetType = TypePair.TargetType;

                var sourceInstance = Expression.Convert( sourceLambdaArg, sourceType );
                var targetInstance = Expression.Convert( targetLambdaArg, targetType );

                var bodyExp = Expression.Invoke( this.MappingExpression,
                    referenceTrack, sourceInstance, targetInstance );

                return _mappingFunc = Expression.Lambda<Action<ReferenceTracking, object, object>>(
                    bodyExp, referenceTrack, sourceLambdaArg, targetLambdaArg ).Compile();
            }
        }

        public MappingSource GetMappingSource( MemberInfo sourceMember,
            LambdaExpression sourceMemberGetterExpression )
        {
            return _sourceProperties.GetOrAdd( sourceMember,
               () => new MappingSource( sourceMemberGetterExpression ) );
        }

        public MappingTarget GetMappingTarget( MemberInfo targetMember,
            LambdaExpression targetMemberGetter, LambdaExpression targetMemberSetter )
        {
            return _targetProperties.GetOrAdd( targetMember,
                () => new MappingTarget( targetMemberSetter, targetMemberGetter ) );
        }

        public MappingSource GetMappingSource( MemberInfo sourceMember,
            MemberAccessPath sourceMemberPath )
        {
            return _sourceProperties.GetOrAdd( sourceMember,
                () => new MappingSource( sourceMemberPath ) );
        }

        public MappingTarget GetMappingTarget( MemberInfo targetMember,
            MemberAccessPath targetMemberPath )
        {
            return _targetProperties.GetOrAdd( targetMember,
                () => new MappingTarget( targetMemberPath ) );
        }
    }
}
