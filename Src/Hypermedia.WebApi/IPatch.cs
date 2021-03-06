﻿using System;
using System.Collections.Generic;
using System.Linq;
using Hypermedia.Metadata;
using Hypermedia.Metadata.Runtime;

namespace Hypermedia.WebApi
{
    public interface IPatch
    {
        /// <summary>
        /// Gets the default resource contract resolver that is to be used for the patching.
        /// </summary>
        IContractResolver ContractResolver { get; }
    }

    public interface IPatch<in T> : IPatch
    {
        /// <summary>
        /// Attempt to patch the given entity.
        /// </summary>
        /// <param name="entity">The entity to apply the patch to.</param>
        /// <param name="contractResolver">The contract resolver to use.</param>
        /// <returns>true if the entity could be patched, false if not.</returns>
        bool TryPatch(T entity, IContractResolver contractResolver);
    }

    public static class PatchExtensions
    {
        /// <summary>
        /// Attempt to patch the given entity.
        /// </summary>
        /// <param name="patch">The patch to perform the operation on.</param>
        /// <param name="entity">The entity to apply the patch to.</param>
        /// <param name="include">The list of columns to include.</param>
        /// <param name="ignore">The list of columns to ignore.</param>
        /// <returns>true if the entity could be patched, false if not.</returns>
        /// <remarks>If the columns to ignore are specified, then all available columns on the type are included by default.</remarks>
        public static bool TryPatch<T>(this IPatch<T> patch, T entity, IReadOnlyList<string> include = null, IReadOnlyList<string> ignore = null)
        {
            if (patch == null)
            {
                throw new ArgumentNullException(nameof(patch));
            }

            if (include == null && ignore == null)
            {
                return patch.TryPatch(entity, patch.ContractResolver);
            }

            if (include != null)
            {
                return TryPatchWithInclude(patch, entity, include);
            }

            return TryPatchWithIgnore(patch, entity, ignore);
        }

        /// <summary>
        /// Attempt to patch the given entity with an available list of columns to include.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="patch">The patch to perform the operation on.</param>
        /// <param name="entity">The entity to apply the patch to.</param>
        /// <param name="includedFields">The list of columns to include in the patch.</param>
        /// <returns>true if the entity could be patched, false if not.</returns>
        static bool TryPatchWithInclude<T>(IPatch<T> patch, T entity, IEnumerable<string> includedFields)
        {
            IContract contract;
            if (patch.ContractResolver.TryResolve(typeof (T), out contract) == false)
            {
                return false;
            }

            // exclude the fields that havent been included
            var fields = contract.Fields.Where(field => includedFields.Contains(field.Name, StringComparer.OrdinalIgnoreCase)).ToList();

            return patch.TryPatch(
                entity, 
                new ContractResolver(new RuntimeContract(contract.Name, contract.ClrType, fields)));
        }

        /// <summary>
        /// Attempt to patch the given entity with all available fields, ignoring the supplied list.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="patch">The patch to perform the operation on.</param>
        /// <param name="entity">The entity to apply the patch to.</param>
        /// <param name="ignoredFields">The list of columns to ignore in the patch.</param>
        /// <returns>true if the entity could be patched, false if not.</returns>
        static bool TryPatchWithIgnore<T>(IPatch<T> patch, T entity, IEnumerable<string> ignoredFields)
        {
            IContract contract;
            if (patch.ContractResolver.TryResolve(typeof(T), out contract) == false)
            {
                return false;
            }

            // exclude the fields that havent been included
            var fields = contract.Fields.Where(field => ignoredFields.Contains(field.Name, StringComparer.OrdinalIgnoreCase) == false).ToList();

            return patch.TryPatch(
                entity,
                new ContractResolver(new RuntimeContract(contract.Name, contract.ClrType, fields)));
        }
    }
}