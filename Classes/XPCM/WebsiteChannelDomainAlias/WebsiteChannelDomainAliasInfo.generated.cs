using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;
using BQ.Xperience.DomainAliases;

[assembly: RegisterObjectType(typeof(WebsiteChannelDomainAliasInfo), WebsiteChannelDomainAliasInfo.OBJECT_TYPE)]

namespace BQ.Xperience.DomainAliases
{
    /// <summary>
    /// Data container class for <see cref="WebsiteChannelDomainAliasInfo"/>.
    /// </summary>
    public partial class WebsiteChannelDomainAliasInfo : AbstractInfo<WebsiteChannelDomainAliasInfo, IInfoProvider<WebsiteChannelDomainAliasInfo>>, IInfoWithId, IInfoWithGuid
    {
        /// <summary>
        /// Object type.
        /// </summary>
        public const string OBJECT_TYPE = "xpcm.websitechanneldomainalias";


        /// <summary>
        /// Type information.
        /// </summary>
#warning "You will need to configure the type info."
        public static readonly ObjectTypeInfo TYPEINFO = new ObjectTypeInfo(typeof(IInfoProvider<WebsiteChannelDomainAliasInfo>), OBJECT_TYPE, "XPCM.WebsiteChannelDomainAlias", "WebsiteChannelDomainAliasId", null, "WebsiteChannelDomainAliasGuid", null, null, null, null, null)
        {
            TouchCacheDependencies = true,
            DependsOn = new List<ObjectDependency>()
            {
                new ObjectDependency("WebsiteChannelDomainAliasChannelId", "cms.channel", ObjectDependencyEnum.Required),
            },
        };


        /// <summary>
        /// Website channel domain alias id.
        /// </summary>
        [DatabaseField]
        public virtual int WebsiteChannelDomainAliasId
        {
            get => ValidationHelper.GetInteger(GetValue(nameof(WebsiteChannelDomainAliasId)), 0);
            set => SetValue(nameof(WebsiteChannelDomainAliasId), value);
        }


        /// <summary>
        /// Website channel domain alias guid.
        /// </summary>
        [DatabaseField]
        public virtual Guid WebsiteChannelDomainAliasGuid
        {
            get => ValidationHelper.GetGuid(GetValue(nameof(WebsiteChannelDomainAliasGuid)), Guid.Empty);
            set => SetValue(nameof(WebsiteChannelDomainAliasGuid), value);
        }


        /// <summary>
        /// Website channel domain alias channel id.
        /// </summary>
        [DatabaseField]
        public virtual int WebsiteChannelDomainAliasChannelId
        {
            get => ValidationHelper.GetInteger(GetValue(nameof(WebsiteChannelDomainAliasChannelId)), 0);
            set => SetValue(nameof(WebsiteChannelDomainAliasChannelId), value);
        }


        /// <summary>
        /// Website channel domain alias domain.
        /// </summary>
        [DatabaseField]
        public virtual string WebsiteChannelDomainAliasDomain
        {
            get => ValidationHelper.GetString(GetValue(nameof(WebsiteChannelDomainAliasDomain)), String.Empty);
            set => SetValue(nameof(WebsiteChannelDomainAliasDomain), value);
        }


        /// <summary>
        /// Deletes the object using appropriate provider.
        /// </summary>
        protected override void DeleteObject()
        {
            Provider.Delete(this);
        }


        /// <summary>
        /// Updates the object using appropriate provider.
        /// </summary>
        protected override void SetObject()
        {
            Provider.Set(this);
        }


        /// <summary>
        /// Creates an empty instance of the <see cref="WebsiteChannelDomainAliasInfo"/> class.
        /// </summary>
        public WebsiteChannelDomainAliasInfo()
            : base(TYPEINFO)
        {
        }


        /// <summary>
        /// Creates a new instances of the <see cref="WebsiteChannelDomainAliasInfo"/> class from the given <see cref="DataRow"/>.
        /// </summary>
        /// <param name="dr">DataRow with the object data.</param>
        public WebsiteChannelDomainAliasInfo(DataRow dr)
            : base(TYPEINFO, dr)
        {
        }
    }
}