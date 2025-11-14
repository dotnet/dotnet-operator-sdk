// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using ConversionWebhookOperator.Entities;

using KubeOps.Operator.Web.Webhooks.Conversion;

namespace ConversionWebhookOperator.Webhooks;

[ConversionWebhook(typeof(V3TestEntity))]
public sealed class TestConversionWebhook : ConversionWebhook<V3TestEntity>
{
    protected override IEnumerable<IEntityConverter<V3TestEntity>> Converters => new IEntityConverter<V3TestEntity>[]
    {
        new V1ToV3(), new V2ToV3(),
    };

    private class V1ToV3 : IEntityConverter<V1TestEntity, V3TestEntity>
    {
        public V3TestEntity Convert(V1TestEntity from)
        {
            var nameSplit = from.Spec.Name.Split(' ');
            var result = new V3TestEntity { Metadata = from.Metadata };
            result.Spec.Firstname = nameSplit[0];
            result.Spec.Lastname = string.Join(' ', nameSplit[1..]);
            return result;
        }

        public V1TestEntity Revert(V3TestEntity to)
        {
            var result = new V1TestEntity { Metadata = to.Metadata };
            result.Spec.Name = $"{to.Spec.Firstname} {to.Spec.Lastname}";
            return result;
        }
    }

    private class V2ToV3 : IEntityConverter<V2TestEntity, V3TestEntity>
    {
        public V3TestEntity Convert(V2TestEntity from)
        {
            var result = new V3TestEntity { Metadata = from.Metadata };
            result.Spec.Firstname = from.Spec.Firstname;
            result.Spec.Lastname = from.Spec.Lastname;
            return result;
        }

        public V2TestEntity Revert(V3TestEntity to)
        {
            var result = new V2TestEntity { Metadata = to.Metadata };
            result.Spec.Firstname = to.Spec.Firstname;
            result.Spec.Lastname = to.Spec.Lastname;
            return result;
        }
    }
}
