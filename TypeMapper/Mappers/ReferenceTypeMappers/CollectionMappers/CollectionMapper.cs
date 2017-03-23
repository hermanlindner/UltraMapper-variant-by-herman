﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using TypeMapper.Configuration;
using TypeMapper.Internals;

namespace TypeMapper.Mappers
{
    /*NOTES:
     * 
     *- Collections that do not implement ICollection<T> must specify which method
     *to use to 'Add' an item or must have a constructor that takes as param an IEnumerable.
     * 
     */

    public class CollectionMapper : ReferenceMapper
    {
        public CollectionMapper( MapperConfiguration configuration )
            : base( configuration ) { }

        public override bool CanHandle( Type source, Type target )
        {
            return source.IsEnumerable() && target.IsEnumerable() &&
                !source.IsBuiltInType( false ) && !target.IsBuiltInType( false ); //avoid strings
        }

        protected override object GetMapperContext( Type source, Type target )
        {
            return new CollectionMapperContext( source, target );
        }

        protected virtual Expression GetSimpleTypeInnerBody( CollectionMapperContext context )
        {
            //- Typically a Costructor(IEnumerable<T>) is faster than AddRange that is faster than Add.
            //  By the way Construcor(capacity) + AddRange has roughly the same performance of Construcor(IEnumerable<T>). 
            //- Must also manage the case where SourceElementType and TargetElementType differ:
            //  cannot use directly the target constructor: use add method or temp collection.

            var typeMapping = MapperConfiguration[ context.SourceCollectionElementType,
                context.TargetCollectionElementType ];

            var constructorInfo = GetTargetCollectionConstructorFromCollection( context );
            if( constructorInfo == null || typeMapping.ReferenceMappingStrategy == ReferenceMappingStrategies.USE_TARGET_INSTANCE_IF_NOT_NULL
                    || context.SourceCollectionElementType != context.TargetCollectionElementType )
            {
                var addMethod = GetTargetCollectionAddMethod( context );
                if( addMethod == null )
                {
                    string msg = $@"Cannot use existing instance on target object. '{nameof( context.TargetInstance.Type )}' does not provide an item-insertion method " +
                        $"Please override '{nameof( GetTargetCollectionAddMethod )}' to provide the item-insertion method.";

                    throw new Exception( msg );
                }

                Expression loopBody = Expression.Call( context.TargetInstance,
                    addMethod, Expression.Invoke( typeMapping.MappingExpression,
                        context.SourceCollectionLoopingVar ) );

                var targetInstanceExpression = GetTargetInstanceAssignment( context );

                return Expression.Block
                (
                    targetInstanceExpression,

                    ExpressionLoops.ForEach( context.SourceInstance,
                        context.SourceCollectionLoopingVar, loopBody )
                );
            }

            var constructor = GetTargetCollectionConstructorFromCollection( context );
            var targetCollectionConstructor = Expression.New( constructor, context.SourceInstance );

            return Expression.Assign( context.TargetInstance, targetCollectionConstructor );
        }

        protected virtual Expression GetComplexTypeInnerBody( CollectionMapperContext context )
        {
            /*
             * By default try to retrieve the item-insertion method of the collection.
             * The exact name of the method can be overridden so that, for example, 
             * on Queue you search for 'Enqueue'. The default method name searched is 'Add'.
             * 
             * If the item-insertion method does not exist, try to retrieve a constructor
             * which takes as its only parameter 'IEnumerable<T>'. If this constructor
             * exists a temporary List<T> is created and then passed to the constructor.
             * 
             * If neither the item insertion method nor the above constructor exist
             * an exception is thrown
             */

            var typeMapping = MapperConfiguration[ context.SourceCollectionElementType,
                context.TargetCollectionElementType ];

            var targetInstanceAssignment = GetTargetInstanceAssignment( context );

            var addMethod = GetTargetCollectionAddMethod( context );
            if( addMethod != null || typeMapping.ReferenceMappingStrategy ==
                ReferenceMappingStrategies.USE_TARGET_INSTANCE_IF_NOT_NULL )
            {
                return Expression.Block
                (
                    targetInstanceAssignment,
                    CollectionLoopWithReferenceTracking( context, context.TargetInstance, addMethod )
                );
            }

            //Look for the constructor which takes an initial collection as parameter
            var constructor = GetTargetCollectionConstructorFromCollection( context );
            if( constructor == null )
            {
                string msg = $@"'{nameof( context.TargetInstance.Type )}' does not provide an 'Add' method or a constructor taking as parameter IEnumerable<T>. " +
                    $"Please override '{nameof( GetTargetCollectionAddMethod )}' to provide the item insertion method.";

                throw new Exception( msg );
            }

            var tempCollectionType = typeof( List<> ).MakeGenericType( context.TargetCollectionElementType );
            var tempCollection = Expression.Parameter( tempCollectionType, "tempCollection" );
            var tempCollectionAddMethod = tempCollectionType.GetMethod( "Add" );

            var tempCtorWithCapacity = tempCollectionType.GetConstructor( new Type[] { typeof( int ) } );
            var tempCollectionCountMethod = context.SourceInstance.Type.GetProperty( "Count" ).GetGetMethod();

            var newTempCollectionExp = Expression.New( tempCtorWithCapacity,
                Expression.Call( context.SourceInstance, tempCollectionCountMethod ) );

            return Expression.Block
            (
                new[] { tempCollection },

                Expression.Assign( tempCollection, newTempCollectionExp ),
                CollectionLoopWithReferenceTracking( context, tempCollection, tempCollectionAddMethod ),
                Expression.Assign( context.TargetInstance, Expression.New( constructor, tempCollection ) )
            );
        }

        protected virtual Expression CollectionLoopWithReferenceTracking( CollectionMapperContext context,
            ParameterExpression targetCollection, MethodInfo targetCollectionAddMethod )
        {
            var itemMapping = MapperConfiguration[ context.SourceCollectionLoopingVar.Type,
                context.TargetCollectionElementType ].MappingExpression;

            var newElement = Expression.Variable( context.TargetCollectionElementType, "newElement" );

            return Expression.Block
            (
                new[] { newElement },

                ExpressionLoops.ForEach( context.SourceInstance, context.SourceCollectionLoopingVar, Expression.Block
                (
                    LookUpBlock( itemMapping, context, context.ReferenceTrack, context.SourceCollectionLoopingVar, newElement ),
                    Expression.Call( targetCollection, targetCollectionAddMethod, newElement )
                )
            ) );
        }

        protected BlockExpression LookUpBlock( LambdaExpression itemMapping, CollectionMapperContext context, ParameterExpression referenceTracker,
           Expression sourceParam, ParameterExpression targetParam )
        {
            Expression cacheLookupCall = Expression.Call( Expression.Constant( refTrackingLookup.Target ),
                refTrackingLookup.Method, referenceTracker, sourceParam,
                    Expression.Constant( targetParam.Type ) );

            Expression cacheInsertCall = Expression.Call( Expression.Constant( addToTracker.Target ),
                addToTracker.Method, referenceTracker, sourceParam,
                Expression.Constant( targetParam.Type ), targetParam );

            return Expression.Block
            (
                Expression.Assign( targetParam, Expression.Convert( cacheLookupCall, targetParam.Type ) ),

                Expression.IfThen
                (
                    Expression.Equal( targetParam, Expression.Constant( null, targetParam.Type ) ),

                    Expression.Block
                    (
                        Expression.Assign( targetParam, Expression.New( targetParam.Type ) ),

                        cacheInsertCall,

                        Expression.Invoke( itemMapping, referenceTracker,
                            sourceParam, targetParam ),

                        Expression.Call
                        (
                            context.ReturnObject, context.AddToReturnList,
                            Expression.New( typeof( ObjectPair ).GetConstructors()[ 0 ],
                            Expression.Convert( sourceParam, typeof( object ) ),
                            Expression.Convert( targetParam, typeof( object ) ) )
                        )
                    )
                )
            );
        }

        protected override Expression GetInnerBody( object contextObj )
        {
            var context = contextObj as CollectionMapperContext;

            if( context.IsTargetElementTypeBuiltIn )
            {
                return Expression.Block
                (
                   GetSimpleTypeInnerBody( context )
                );
            }
            else
            {
                var getCountMethod = context.SourceInstance.Type.GetProperty( "Count" ).GetGetMethod();

                return Expression.Block
                (
                    Expression.Assign( context.ReturnObject, Expression.New( context.ReturnTypeConstructor,
                        Expression.Call( context.SourceInstance, getCountMethod ) ) ),

                    GetComplexTypeInnerBody( context )                    
                );
            }
        }

        protected virtual ConstructorInfo GetTargetCollectionConstructorFromCollection( CollectionMapperContext context )
        {
            var paramType = new Type[] { typeof( IEnumerable<> )
                .MakeGenericType( context.TargetCollectionElementType ) };

            return context.TargetInstance.Type.GetConstructor( paramType );
        }

        /// <summary>
        /// Return the method that allows to add items to the target collection.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        protected virtual MethodInfo GetTargetCollectionAddMethod( CollectionMapperContext context )
        {
            return context.TargetInstance.Type.GetMethod( "Add" );
        }

        protected override Expression GetTargetInstanceAssignment( object contextObj )
        {
            var context = contextObj as CollectionMapperContext;
            var typeMapping = MapperConfiguration[ context.SourceInstance.Type,
                context.TargetInstance.Type ];

            if( typeMapping.ReferenceMappingStrategy == ReferenceMappingStrategies.CREATE_NEW_INSTANCE
                && context.SourceInstance.Type.ImplementsInterface( typeof( ICollection<> ) )
                && context.TargetInstance.Type.ImplementsInterface( typeof( ICollection<> ) ) )
            {
                var constructorWithCapacity = context.TargetInstance.Type.GetConstructor( new Type[] { typeof( int ) } );
                if( constructorWithCapacity != null )
                {
                    //ICollection<int> is used only because it is forbidden to use nameof with unbound generic types.
                    //Any other type instead of int would work.
                    var getCountMethod = context.SourceInstance.Type.GetProperty( nameof( ICollection<int>.Count ) ).GetGetMethod();
                    return Expression.Assign( context.TargetInstance, Expression.New( constructorWithCapacity,
                        Expression.Call( context.SourceInstance, getCountMethod ) ) );
                }
            }

            return base.GetTargetInstanceAssignment( contextObj );
        }
    }
}

