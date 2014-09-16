﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Xml.Linq;
using Umbraco.Core.Configuration;
using Umbraco.Core.IO;
using Umbraco.Core.Models.EntityBase;
using Umbraco.Core.Models;
using Umbraco.Core.Models.Rdbms;
using Umbraco.Core.Persistence.Caching;
using Umbraco.Core.Persistence.DatabaseModelDefinitions;
using Umbraco.Core.Persistence.Factories;
using Umbraco.Core.Persistence.Querying;
using Umbraco.Core.Persistence.Relators;
using Umbraco.Core.Persistence.SqlSyntax;
using Umbraco.Core.Persistence.UnitOfWork;
using Umbraco.Core.Dynamics;

namespace Umbraco.Core.Persistence.Repositories
{
    /// <summary>
    /// Represents a repository for doing CRUD operations for <see cref="IMember"/>
    /// </summary>
    internal class MemberRepository : VersionableRepositoryBase<int, IMember>, IMemberRepository
    {
        private readonly IMemberTypeRepository _memberTypeRepository;
        private readonly ITagRepository _tagRepository;
        private readonly IMemberGroupRepository _memberGroupRepository;
        private readonly ContentXmlRepository<IMember> _contentXmlRepository;
        private readonly ContentPreviewRepository<IMember> _contentPreviewRepository;

        public MemberRepository(IDatabaseUnitOfWork work, IMemberTypeRepository memberTypeRepository, IMemberGroupRepository memberGroupRepository, ITagRepository tagRepository)
            : base(work)
        {
            if (memberTypeRepository == null) throw new ArgumentNullException("memberTypeRepository");
            if (tagRepository == null) throw new ArgumentNullException("tagRepository");
            _memberTypeRepository = memberTypeRepository;
            _tagRepository = tagRepository;
            _memberGroupRepository = memberGroupRepository;
            _contentXmlRepository = new ContentXmlRepository<IMember>(work, NullCacheProvider.Current);
            _contentPreviewRepository = new ContentPreviewRepository<IMember>(work, NullCacheProvider.Current);
        }

        public MemberRepository(IDatabaseUnitOfWork work, IRepositoryCacheProvider cache, IMemberTypeRepository memberTypeRepository, IMemberGroupRepository memberGroupRepository, ITagRepository tagRepository)
            : base(work, cache)
        {
            if (memberTypeRepository == null) throw new ArgumentNullException("memberTypeRepository");
            if (tagRepository == null) throw new ArgumentNullException("tagRepository");
            _memberTypeRepository = memberTypeRepository;
            _tagRepository = tagRepository;
            _memberGroupRepository = memberGroupRepository;
            _contentXmlRepository = new ContentXmlRepository<IMember>(work, NullCacheProvider.Current);
            _contentPreviewRepository = new ContentPreviewRepository<IMember>(work, NullCacheProvider.Current);
        }

        #region Overrides of RepositoryBase<int, IMembershipUser>

        protected override IMember PerformGet(int id)
        {
            var sql = GetBaseQuery(false);
            sql.Where(GetBaseWhereClause(), new { Id = id });
            sql.OrderByDescending<ContentVersionDto>(x => x.VersionDate);

            var dto = Database.Fetch<MemberDto, ContentVersionDto, ContentDto, NodeDto>(sql).FirstOrDefault();

            if (dto == null)
                return null;

            var content = CreateMemberFromDto(dto, dto.ContentVersionDto.VersionId, sql);

            return content;

            //var dtos =
            //    Database.Fetch<MemberReadOnlyDto, PropertyDataReadOnlyDto, MemberReadOnlyDto>(
            //        new PropertyDataRelator().Map, sql);

            //return BuildFromDto(dtos, sql);
        }

        protected override IEnumerable<IMember> PerformGetAll(params int[] ids)
        {
            var sql = GetBaseQuery(false);
            if (ids.Any())
            {
                sql.Where("umbracoNode.id in (@ids)", new { ids = ids });
            }

            return ProcessQuery(sql);

            //var sql = GetBaseQuery(false);
            //if (ids.Any())
            //{
            //    var statement = string.Join(" OR ", ids.Select(x => string.Format("umbracoNode.id='{0}'", x)));
            //    sql.Where(statement);
            //}
            //sql.OrderByDescending<ContentVersionDto>(x => x.VersionDate);

            //var dtos =
            //    Database.Fetch<MemberReadOnlyDto, PropertyDataReadOnlyDto, MemberReadOnlyDto>(
            //        new PropertyDataRelator().Map, sql);

            //return BuildFromDtos(dtos, sql);
        }

        protected override IEnumerable<IMember> PerformGetByQuery(IQuery<IMember> query)
        {
            var baseQuery = GetBaseQuery(false);

            //check if the query is based on properties or not
            var query1 = query as Query<IMember>;
            if (query1 == null)
                throw new Exception("Query cannot be null");
            var wheres = query1.WhereClauses();
            //this is a pretty rudimentary check but wil work, we just need to know if this query requires property
            // level queries
            if (wheres.Any(x => x.Contains("cmsPropertyType")))
            {
                var sqlWithProps = GetNodeIdQueryWithPropertyData();
                var translator = new SqlTranslator<IMember>(sqlWithProps, query);
                var sql = translator.Translate();

                baseQuery.Append(new Sql("WHERE umbracoNode.id IN (" + sql.SQL + ")", sql.Arguments))
                    .OrderBy<NodeDto>(x => x.SortOrder);

                return ProcessQuery(baseQuery);    
            }
            else
            {
                var translator = new SqlTranslator<IMember>(baseQuery, query);
                var sql = translator.Translate()
                    .OrderBy<NodeDto>(x => x.SortOrder);

                return ProcessQuery(sql);    
            }

            

            //var sqlSubquery = GetSubquery();
            //var translator = new SqlTranslator<IMember>(sqlSubquery, query);
            //var subquery = translator.Translate();
            //var sql = GetBaseQuery(false)
            //    .Append(new Sql("WHERE umbracoNode.id IN (" + subquery.SQL + ")", subquery.Arguments))
            //    .OrderByDescending<ContentVersionDto>(x => x.VersionDate)
            //    .OrderBy<NodeDto>(x => x.SortOrder);

            //var dtos =
            //    Database.Fetch<MemberReadOnlyDto, PropertyDataReadOnlyDto, MemberReadOnlyDto>(
            //        new PropertyDataRelator().Map, sql);

            //return BuildFromDtos(dtos, sql);
        }

        #endregion

        #region Overrides of PetaPocoRepositoryBase<int,IMembershipUser>

        protected override Sql GetBaseQuery(bool isCount)
        {
            var sql = new Sql();
            sql.Select(isCount ? "COUNT(*)" : "*")
                .From<MemberDto>()
                .InnerJoin<ContentVersionDto>()
                .On<ContentVersionDto, MemberDto>(left => left.NodeId, right => right.NodeId)                
                .InnerJoin<ContentDto>()
                .On<ContentVersionDto, ContentDto>(left => left.NodeId, right => right.NodeId)                
                .InnerJoin<NodeDto>()
                .On<ContentDto, NodeDto>(left => left.NodeId, right => right.NodeId)
                .Where<NodeDto>(x => x.NodeObjectType == NodeObjectTypeId);
            return sql;

            //var sql = new Sql();

            //if (isCount)
            //{
            //    sql.Select("COUNT(*)")
            //        .From<NodeDto>()
            //        .InnerJoin<ContentDto>().On<ContentDto, NodeDto>(left => left.NodeId, right => right.NodeId)
            //        .InnerJoin<ContentTypeDto>().On<ContentTypeDto, ContentDto>(left => left.NodeId, right => right.ContentTypeId)
            //        .InnerJoin<ContentVersionDto>().On<ContentVersionDto, NodeDto>(left => left.NodeId, right => right.NodeId)
            //        .InnerJoin<MemberDto>().On<MemberDto, ContentDto>(left => left.NodeId, right => right.NodeId)
            //        .Where<NodeDto>(x => x.NodeObjectType == NodeObjectTypeId);
            //    return sql;
            //}

            //sql.Select("umbracoNode.*", "cmsContent.contentType", "cmsContentType.alias AS ContentTypeAlias", "cmsContentVersion.VersionId",
            //    "cmsContentVersion.VersionDate", "cmsContentVersion.LanguageLocale", "cmsMember.Email",
            //    "cmsMember.LoginName", "cmsMember.Password", "cmsPropertyData.id AS PropertyDataId", "cmsPropertyData.propertytypeid", 
            //    "cmsPropertyData.dataDate", "cmsPropertyData.dataInt", "cmsPropertyData.dataNtext", "cmsPropertyData.dataNvarchar",
            //    "cmsPropertyType.id", "cmsPropertyType.Alias", "cmsPropertyType.Description",
            //    "cmsPropertyType.Name", "cmsPropertyType.mandatory", "cmsPropertyType.validationRegExp",
            //    "cmsPropertyType.helpText", "cmsPropertyType.sortOrder AS PropertyTypeSortOrder", "cmsPropertyType.propertyTypeGroupId",
            //    "cmsPropertyType.dataTypeId", "cmsDataType.propertyEditorAlias", "cmsDataType.dbType")
            //    .From<NodeDto>()
            //    .InnerJoin<ContentDto>().On<ContentDto, NodeDto>(left => left.NodeId, right => right.NodeId)
            //    .InnerJoin<ContentTypeDto>().On<ContentTypeDto, ContentDto>(left => left.NodeId, right => right.ContentTypeId)
            //    .InnerJoin<ContentVersionDto>().On<ContentVersionDto, NodeDto>(left => left.NodeId, right => right.NodeId)
            //    .InnerJoin<MemberDto>().On<MemberDto, ContentDto>(left => left.NodeId, right => right.NodeId)
            //    .LeftJoin<PropertyTypeDto>().On<PropertyTypeDto, ContentDto>(left => left.ContentTypeId, right => right.ContentTypeId)
            //    .LeftJoin<DataTypeDto>().On<DataTypeDto, PropertyTypeDto>(left => left.DataTypeId, right => right.DataTypeId)
            //    .LeftJoin<PropertyDataDto>().On<PropertyDataDto, PropertyTypeDto>(left => left.PropertyTypeId, right => right.Id)
            //    .Append("AND cmsPropertyData.versionId = cmsContentVersion.VersionId")
            //    .Where<NodeDto>(x => x.NodeObjectType == NodeObjectTypeId);
            //return sql;
        }

        protected override string GetBaseWhereClause()
        {
            return "umbracoNode.id = @Id";
        }

        protected Sql GetNodeIdQueryWithPropertyData()
        {
            var sql = new Sql();
            sql.Select("DISTINCT(umbracoNode.id)")
                .From<NodeDto>()
                .InnerJoin<ContentDto>().On<ContentDto, NodeDto>(left => left.NodeId, right => right.NodeId)
                .InnerJoin<ContentTypeDto>().On<ContentTypeDto, ContentDto>(left => left.NodeId, right => right.ContentTypeId)
                .InnerJoin<ContentVersionDto>().On<ContentVersionDto, NodeDto>(left => left.NodeId, right => right.NodeId)
                .InnerJoin<MemberDto>().On<MemberDto, ContentDto>(left => left.NodeId, right => right.NodeId)
                .LeftJoin<PropertyTypeDto>().On<PropertyTypeDto, ContentDto>(left => left.ContentTypeId, right => right.ContentTypeId)
                .LeftJoin<DataTypeDto>().On<DataTypeDto, PropertyTypeDto>(left => left.DataTypeId, right => right.DataTypeId)
                .LeftJoin<PropertyDataDto>().On<PropertyDataDto, PropertyTypeDto>(left => left.PropertyTypeId, right => right.Id)
                .Append("AND cmsPropertyData.versionId = cmsContentVersion.VersionId")
                .Where<NodeDto>(x => x.NodeObjectType == NodeObjectTypeId);
            return sql;
        }

        protected override IEnumerable<string> GetDeleteClauses()
        {
            var list = new List<string>
                           {
                               "DELETE FROM cmsTask WHERE nodeId = @Id",
                               "DELETE FROM umbracoUser2NodeNotify WHERE nodeId = @Id",
                               "DELETE FROM umbracoUser2NodePermission WHERE nodeId = @Id",
                               "DELETE FROM umbracoRelation WHERE parentId = @Id",
                               "DELETE FROM umbracoRelation WHERE childId = @Id",
                               "DELETE FROM cmsTagRelationship WHERE nodeId = @Id",
                               "DELETE FROM cmsPropertyData WHERE contentNodeId = @Id",
                               "DELETE FROM cmsMember2MemberGroup WHERE Member = @Id",
                               "DELETE FROM cmsMember WHERE nodeId = @Id",
                               "DELETE FROM cmsContentVersion WHERE ContentId = @Id",
                               "DELETE FROM cmsContentXml WHERE nodeId = @Id",
                               "DELETE FROM cmsContent WHERE nodeId = @Id",
                               "DELETE FROM umbracoNode WHERE id = @Id"
                           };
            return list;
        }

        protected override Guid NodeObjectTypeId
        {
            get { return new Guid(Constants.ObjectTypes.Member); }
        }

        #endregion

        #region Unit of Work Implementation

        protected override void PersistNewItem(IMember entity)
        {
            ((Member)entity).AddingEntity();

            //Ensure that strings don't contain characters that are invalid in XML
            entity.SanitizeEntityPropertiesForXmlStorage();

            var factory = new MemberFactory(NodeObjectTypeId, entity.Id);
            var dto = factory.BuildDto(entity);

            //NOTE Should the logic below have some kind of fallback for empty parent ids ?
            //Logic for setting Path, Level and SortOrder
            var parent = Database.First<NodeDto>("WHERE id = @ParentId", new { ParentId = ((IUmbracoEntity)entity).ParentId });
            int level = parent.Level + 1;
            int sortOrder =
                Database.ExecuteScalar<int>("SELECT COUNT(*) FROM umbracoNode WHERE parentID = @ParentId AND nodeObjectType = @NodeObjectType",
                                                      new { ParentId = ((IUmbracoEntity)entity).ParentId, NodeObjectType = NodeObjectTypeId });

            //Create the (base) node data - umbracoNode
            var nodeDto = dto.ContentVersionDto.ContentDto.NodeDto;
            nodeDto.Path = parent.Path;
            nodeDto.Level = short.Parse(level.ToString(CultureInfo.InvariantCulture));
            nodeDto.SortOrder = sortOrder;
            var o = Database.IsNew(nodeDto) ? Convert.ToInt32(Database.Insert(nodeDto)) : Database.Update(nodeDto);

            //Update with new correct path
            nodeDto.Path = string.Concat(parent.Path, ",", nodeDto.NodeId);
            Database.Update(nodeDto);

            //Update entity with correct values
            entity.Id = nodeDto.NodeId; //Set Id on entity to ensure an Id is set
            ((IUmbracoEntity)entity).Path = nodeDto.Path;
            ((IUmbracoEntity)entity).SortOrder = sortOrder;
            ((IUmbracoEntity)entity).Level = level;

            //Create the Content specific data - cmsContent
            var contentDto = dto.ContentVersionDto.ContentDto;
            contentDto.NodeId = nodeDto.NodeId;
            Database.Insert(contentDto);

            //Create the first version - cmsContentVersion
            //Assumes a new Version guid and Version date (modified date) has been set
            dto.ContentVersionDto.NodeId = nodeDto.NodeId;
            Database.Insert(dto.ContentVersionDto);

            //Create the first entry in cmsMember
            dto.NodeId = nodeDto.NodeId;
            Database.Insert(dto);

            //Create the PropertyData for this version - cmsPropertyData
            var propertyFactory = new PropertyFactory(entity.ContentType, entity.Version, entity.Id);
            //Add Properties
            // - don't try to save the property if it doesn't exist (or doesn't have an ID) on the content type
            // - this can occur if the member type doesn't contain the built-in properties that the
            // - member object contains.        
            var propsToPersist = entity.Properties.Where(x => x.PropertyType.HasIdentity).ToArray();
            var propertyDataDtos = propertyFactory.BuildDto(propsToPersist);
            var keyDictionary = new Dictionary<int, int>();

            //Add Properties
            foreach (var propertyDataDto in propertyDataDtos)
            {
                var primaryKey = Convert.ToInt32(Database.Insert(propertyDataDto));
                keyDictionary.Add(propertyDataDto.PropertyTypeId, primaryKey);
            }

            //Update Properties with its newly set Id
            foreach (var property in propsToPersist)
            {
                property.Id = keyDictionary[property.PropertyTypeId];
            }

            UpdatePropertyTags(entity, _tagRepository);

            ((Member)entity).ResetDirtyProperties();
        }

        protected override void PersistUpdatedItem(IMember entity)
        {
            //Updates Modified date
            ((Member)entity).UpdatingEntity();

            //Ensure that strings don't contain characters that are invalid in XML
            entity.SanitizeEntityPropertiesForXmlStorage();

            var dirtyEntity = (ICanBeDirty) entity;

            //Look up parent to get and set the correct Path and update SortOrder if ParentId has changed
            if (dirtyEntity.IsPropertyDirty("ParentId"))
            {
                var parent = Database.First<NodeDto>("WHERE id = @ParentId", new { ParentId = ((IUmbracoEntity)entity).ParentId });
                ((IUmbracoEntity)entity).Path = string.Concat(parent.Path, ",", entity.Id);
                ((IUmbracoEntity)entity).Level = parent.Level + 1;
                var maxSortOrder =
                    Database.ExecuteScalar<int>(
                        "SELECT coalesce(max(sortOrder),0) FROM umbracoNode WHERE parentid = @ParentId AND nodeObjectType = @NodeObjectType",
                        new { ParentId = ((IUmbracoEntity)entity).ParentId, NodeObjectType = NodeObjectTypeId });
                ((IUmbracoEntity)entity).SortOrder = maxSortOrder + 1;
            }

            var factory = new MemberFactory(NodeObjectTypeId, entity.Id);
            //Look up Content entry to get Primary for updating the DTO
            var contentDto = Database.SingleOrDefault<ContentDto>("WHERE nodeId = @Id", new { Id = entity.Id });
            factory.SetPrimaryKey(contentDto.PrimaryKey);
            var dto = factory.BuildDto(entity);

            //Updates the (base) node data - umbracoNode
            var nodeDto = dto.ContentVersionDto.ContentDto.NodeDto;
            var o = Database.Update(nodeDto);

            //Only update this DTO if the contentType has actually changed
            if (contentDto.ContentTypeId != ((Member)entity).ContentTypeId)
            {
                //Create the Content specific data - cmsContent
                var newContentDto = dto.ContentVersionDto.ContentDto;
                Database.Update(newContentDto);
            }

            //In order to update the ContentVersion we need to retrieve its primary key id
            var contentVerDto = Database.SingleOrDefault<ContentVersionDto>("WHERE VersionId = @Version", new { Version = entity.Version });
            dto.ContentVersionDto.Id = contentVerDto.Id;
            //Updates the current version - cmsContentVersion
            //Assumes a Version guid exists and Version date (modified date) has been set/updated
            Database.Update(dto.ContentVersionDto);
            
            //Updates the cmsMember entry if it has changed
            
            //NOTE: these cols are the REAL column names in the db
            var changedCols = new List<string>();

            if (dirtyEntity.IsPropertyDirty("Email"))
            {
                changedCols.Add("Email");
            }
            if (dirtyEntity.IsPropertyDirty("Username"))
            {
                changedCols.Add("LoginName");
            }
            // DO NOT update the password if it has not changed or if it is null or empty
            if (dirtyEntity.IsPropertyDirty("RawPasswordValue") && entity.RawPasswordValue.IsNullOrWhiteSpace() == false)
            {
                changedCols.Add("Password");
            }
            //only update the changed cols
            if (changedCols.Count > 0)
            {
                Database.Update(dto, changedCols);    
            }

            //TODO ContentType for the Member entity

            //Create the PropertyData for this version - cmsPropertyData
            var propertyFactory = new PropertyFactory(entity.ContentType, entity.Version, entity.Id);            
            var keyDictionary = new Dictionary<int, int>();

            //Add Properties
            // - don't try to save the property if it doesn't exist (or doesn't have an ID) on the content type
            // - this can occur if the member type doesn't contain the built-in properties that the
            // - member object contains.            
            var propsToPersist = entity.Properties.Where(x => x.PropertyType.HasIdentity).ToArray();

            var propertyDataDtos = propertyFactory.BuildDto(propsToPersist);

            foreach (var propertyDataDto in propertyDataDtos)
            {
                if (propertyDataDto.Id > 0)
                {
                    Database.Update(propertyDataDto);
                }
                else
                {
                    int primaryKey = Convert.ToInt32(Database.Insert(propertyDataDto));
                    keyDictionary.Add(propertyDataDto.PropertyTypeId, primaryKey);
                }
            }

            //Update Properties with its newly set Id
            if (keyDictionary.Any())
            {
                foreach (var property in ((Member)entity).Properties)
                {
                    property.Id = keyDictionary[property.PropertyTypeId];
                }
            }

            UpdatePropertyTags(entity, _tagRepository);

            dirtyEntity.ResetDirtyProperties();
        }

        protected override void PersistDeletedItem(IMember entity)
        {
            var fs = FileSystemProviderManager.Current.GetFileSystemProvider<MediaFileSystem>();
            var uploadFieldAlias = Constants.PropertyEditors.UploadFieldAlias;
            //Loop through properties to check if the media item contains images/file that should be deleted
            foreach (var property in ((Member)entity).Properties)
            {
                if (property.PropertyType.PropertyEditorAlias == uploadFieldAlias &&
                    string.IsNullOrEmpty(property.Value.ToString()) == false
                    && fs.FileExists(fs.GetRelativePath(property.Value.ToString())))
                {
                    var relativeFilePath = fs.GetRelativePath(property.Value.ToString());
                    var parentDirectory = System.IO.Path.GetDirectoryName(relativeFilePath);

                    // don't want to delete the media folder if not using directories.
                    if (UmbracoConfig.For.UmbracoSettings().Content.UploadAllowDirectories && parentDirectory != fs.GetRelativePath("/"))
                    {
                        //issue U4-771: if there is a parent directory the recursive parameter should be true
                        fs.DeleteDirectory(parentDirectory, String.IsNullOrEmpty(parentDirectory) == false);
                    }
                    else
                    {
                        fs.DeleteFile(relativeFilePath, true);
                    }
                }
            }

            base.PersistDeletedItem(entity);
        }

        #endregion

        #region Overrides of VersionableRepositoryBase<IMembershipUser>

        public override IMember GetByVersion(Guid versionId)
        {
            var sql = GetBaseQuery(false);
            sql.Where("cmsContentVersion.VersionId = @VersionId", new { VersionId = versionId });
            sql.OrderByDescending<ContentVersionDto>(x => x.VersionDate);

            var dto = Database.Fetch<MemberDto, ContentVersionDto, ContentDto, NodeDto>(sql).FirstOrDefault();

            if (dto == null)
                return null;

            var memberType = _memberTypeRepository.Get(dto.ContentVersionDto.ContentDto.ContentTypeId);

            var factory = new MemberFactory(memberType, NodeObjectTypeId, dto.NodeId);
            var media = factory.BuildEntity(dto);

            var properties = GetPropertyCollection(sql, new DocumentDefinition(dto.NodeId, dto.ContentVersionDto.VersionId, media.UpdateDate, media.CreateDate, memberType));

            media.Properties = properties[dto.NodeId];

            //on initial construction we don't want to have dirty properties tracked
            // http://issues.umbraco.org/issue/U4-1946
            ((Entity)media).ResetDirtyProperties(false);
            return media;

            //var sql = GetBaseQuery(false);
            //sql.Where<ContentVersionDto>(x => x.VersionId == versionId);
            //sql.OrderByDescending<ContentVersionDto>(x => x.VersionDate);

            //var dtos =
            //    Database.Fetch<MemberReadOnlyDto, PropertyDataReadOnlyDto, MemberReadOnlyDto>(
            //        new PropertyDataRelator().Map, sql);

            //return BuildFromDto(dtos, sql);
        }

        protected override void PerformDeleteVersion(int id, Guid versionId)
        {
            Database.Delete<PreviewXmlDto>("WHERE nodeId = @Id AND versionId = @VersionId", new { Id = id, VersionId = versionId });
            Database.Delete<PropertyDataDto>("WHERE contentNodeId = @Id AND versionId = @VersionId", new { Id = id, VersionId = versionId });
            Database.Delete<ContentVersionDto>("WHERE ContentId = @Id AND VersionId = @VersionId", new { Id = id, VersionId = versionId });
        }

        #endregion

        public IEnumerable<IMember> FindMembersInRole(string roleName, string usernameToMatch, StringPropertyMatchType matchType = StringPropertyMatchType.StartsWith)
        {
            //get the group id
            var grpQry = new Query<IMemberGroup>().Where(group => group.Name.Equals(roleName));
            var memberGroup = _memberGroupRepository.GetByQuery(grpQry).FirstOrDefault();
            if (memberGroup == null) return Enumerable.Empty<IMember>();

            // get the members by username
            var query = new Query<IMember>();
            switch (matchType)
            {
                case StringPropertyMatchType.Exact:
                    query.Where(member => member.Username.Equals(usernameToMatch));
                    break;
                case StringPropertyMatchType.Contains:
                    query.Where(member => member.Username.Contains(usernameToMatch));
                    break;
                case StringPropertyMatchType.StartsWith:
                    query.Where(member => member.Username.StartsWith(usernameToMatch));
                    break;
                case StringPropertyMatchType.EndsWith:
                    query.Where(member => member.Username.EndsWith(usernameToMatch));
                    break;
                case StringPropertyMatchType.Wildcard:
                    query.Where(member => member.Username.SqlWildcard(usernameToMatch, TextColumnType.NVarchar));
                    break;
                default:
                    throw new ArgumentOutOfRangeException("matchType");
            }
            var matchedMembers = GetByQuery(query).ToArray();

            var membersInGroup = new List<IMember>();
            //then we need to filter the matched members that are in the role
            //since the max sql params are 2100 on sql server, we'll reduce that to be safe for potentially other servers and run the queries in batches
            var inGroups = matchedMembers.InGroupsOf(1000);
            foreach (var batch in inGroups)
            {
                var memberIdBatch = batch.Select(x => x.Id);
                var sql = new Sql().Select("*").From<Member2MemberGroupDto>()
                    .Where<Member2MemberGroupDto>(dto => dto.MemberGroup == memberGroup.Id)
                    .Where("Member IN (@memberIds)", new { memberIds = memberIdBatch });
                var memberIdsInGroup = Database.Fetch<Member2MemberGroupDto>(sql)
                    .Select(x => x.Member).ToArray();

                membersInGroup.AddRange(matchedMembers.Where(x => memberIdsInGroup.Contains(x.Id)));
            }

            return membersInGroup;

        }

        /// <summary>
        /// Get all members in a specific group
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        public IEnumerable<IMember> GetByMemberGroup(string groupName)
        {
            var grpQry = new Query<IMemberGroup>().Where(group => group.Name.Equals(groupName));
            var memberGroup = _memberGroupRepository.GetByQuery(grpQry).FirstOrDefault();
            if (memberGroup == null) return Enumerable.Empty<IMember>();
            
            var subQuery = new Sql().Select("Member").From<Member2MemberGroupDto>().Where<Member2MemberGroupDto>(dto => dto.MemberGroup == memberGroup.Id);

            var sql = GetBaseQuery(false)
                //TODO: This is NOT great!, do an inner join!
                .Append(new Sql("WHERE umbracoNode.id IN (" + subQuery.SQL + ")", subQuery.Arguments))
                .OrderByDescending<ContentVersionDto>(x => x.VersionDate)
                .OrderBy<NodeDto>(x => x.SortOrder);
            
            return ProcessQuery(sql);

            //var dtos =
            //    Database.Fetch<MemberReadOnlyDto, PropertyDataReadOnlyDto, MemberReadOnlyDto>(
            //        new PropertyDataRelator().Map, sql);

            //return BuildFromDtos(dtos, sql);
        }

        public bool Exists(string username)
        {
            var sql = new Sql();
            var escapedUserName = PetaPocoExtensions.EscapeAtSymbols(username);
            sql.Select("COUNT(*)")
                .From<MemberDto>()
                .Where<MemberDto>(x => x.LoginName == escapedUserName);

            return Database.ExecuteScalar<int>(sql) > 0;
        }

        public int GetCountByQuery(IQuery<IMember> query)
        {
            var sqlWithProps = GetNodeIdQueryWithPropertyData();
            var translator = new SqlTranslator<IMember>(sqlWithProps, query);
            var sql = translator.Translate();

            //get the COUNT base query
            var fullSql = GetBaseQuery(true)
                .Append(new Sql("WHERE umbracoNode.id IN (" + sql.SQL + ")", sql.Arguments));
            
            return Database.ExecuteScalar<int>(fullSql);
        }

        /// <summary>
        /// Gets paged member results
        /// </summary>
        /// <param name="query">
        /// The where clause, if this is null all records are queried
        /// </param>
        /// <param name="pageIndex"></param>
        /// <param name="pageSize"></param>
        /// <param name="totalRecords"></param>
        /// <param name="orderBy"></param>
        /// <param name="orderDirection"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        /// <remarks>
        /// The query supplied will ONLY work with data specifically on the cmsMember table because we are using PetaPoco paging (SQL paging)
        /// </remarks>
        public IEnumerable<IMember> GetPagedResultsByQuery(IQuery<IMember> query, int pageIndex, int pageSize, out int totalRecords,
            string orderBy, Direction orderDirection, string filter = "")
        {
            if (orderBy == null) throw new ArgumentNullException("orderBy");

            // Get base query
            var sqlClause = GetBaseQuery(false);

            if (query == null) query = new Query<IMember>();
            var translator = new SqlTranslator<IMember>(sqlClause, query);
            var sql = translator.Translate();

            // Apply filter
            if (string.IsNullOrEmpty(filter) == false)
            {
                //TODO: Maybe other filters?
                sql = sql.Where("cmsMember.LoginName LIKE @0", "%" + filter + "%");
            }

            // Apply order according to parameters
            if (string.IsNullOrEmpty(orderBy) == false)
            {
                var orderByParams = new[] { GetDatabaseFieldNameForOrderBy( orderBy) };
                if (orderDirection == Direction.Ascending)
                {
                    sql = sql.OrderBy(orderByParams);
                }
                else
                {
                    sql = sql.OrderByDescending(orderByParams);
                }
            }

            // Note we can't do multi-page for several DTOs like we can multi-fetch and are doing in PerformGetByQuery, 
            // but actually given we are doing a Get on each one (again as in PerformGetByQuery), we only need the node Id.
            // So we'll modify the SQL.
            var modifiedSQL = sql.SQL.Replace("SELECT *", "SELECT cmsMember.nodeId");

            // Get page of results and total count
            IEnumerable<IMember> result;
            var pagedResult = Database.Page<MemberDto>(pageIndex + 1, pageSize, modifiedSQL, sql.Arguments);
            totalRecords = Convert.ToInt32(pagedResult.TotalItems);
            if (totalRecords > 0)
            {
                // Parse out node Ids and load content (we need the cast here in order to be able to call the IQueryable extension
                // methods OrderBy or OrderByDescending)
                var content = GetAll(pagedResult.Items
                    .DistinctBy(x => x.NodeId)
                    .Select(x => x.NodeId).ToArray())
                    .Cast<Member>()
                    .AsQueryable();

                // Now we need to ensure this result is also ordered by the same order by clause
                var orderByProperty = GetEntityPropertyNameForOrderBy(orderBy);
                if (orderDirection == Direction.Ascending)
                {
                    result = content.OrderBy(orderByProperty);
                }
                else
                {
                    result = content.OrderByDescending(orderByProperty);
                }
            }
            else
            {
                result = Enumerable.Empty<IMember>();
            }

            return result;
        }

        //public IEnumerable<IMember> GetPagedResultsByQuery<TDto>(
        //    Sql sql, int pageIndex, int pageSize, out int totalRecords,
        //    Func<IEnumerable<TDto>, int[]> resolveIds)
        //{
        //    ////If a max size is passed in, just do a normal get all!
        //    //if (pageSize == int.MaxValue)
        //    //{
        //    //    var result = GetAll().ToArray();
        //    //    totalRecords = result.Count();
        //    //    return result;
        //    //}

        //    //// Get base query
        //    //var sqlClause = GetBaseQuery(false);
        //    //var translator = new SqlTranslator<IContent>(sqlClause, query);
        //    //var sql = translator.Translate()
        //    //                    .Where<DocumentDto>(x => x.Newest);

        //    //// Apply filter
        //    //if (!string.IsNullOrEmpty(filter))
        //    //{
        //    //    sql = sql.Where("cmsDocument.text LIKE @0", "%" + filter + "%");
        //    //}

        //    //// Apply order according to parameters
        //    //if (!string.IsNullOrEmpty(orderBy))
        //    //{
        //    //    var orderByParams = new[] { GetDatabaseFieldNameForOrderBy(orderBy) };
        //    //    if (orderDirection == Direction.Ascending)
        //    //    {
        //    //        sql = sql.OrderBy(orderByParams);
        //    //    }
        //    //    else
        //    //    {
        //    //        sql = sql.OrderByDescending(orderByParams);
        //    //    }
        //    //}

        //    var pagedResult = Database.Page<TDto>(pageIndex + 1, pageSize, sql);

        //    totalRecords = Convert.ToInt32(pagedResult.TotalItems);

        //    //now that we have the member dto's we need to construct true members from the list.
        //    if (totalRecords == 0)
        //    {
        //        return Enumerable.Empty<IMember>();
        //    }
        //    return GetAll(resolveIds(pagedResult.Items)).ToArray();
        //}

        public void AddOrUpdateContentXml(IMember content, Func<IMember, XElement> xml)
        {
            var contentExists = Database.ExecuteScalar<int>("SELECT COUNT(nodeId) FROM cmsContentXml WHERE nodeId = @Id", new { Id = content.Id }) != 0;

            _contentXmlRepository.AddOrUpdate(new ContentXmlEntity<IMember>(contentExists, content, xml));
        }

        public void AddOrUpdatePreviewXml(IMember content, Func<IMember, XElement> xml)
        {
            var previewExists =
                    Database.ExecuteScalar<int>("SELECT COUNT(nodeId) FROM cmsPreviewXml WHERE nodeId = @Id AND versionId = @Version",
                                                    new { Id = content.Id, Version = content.Version }) != 0;

            _contentPreviewRepository.AddOrUpdate(new ContentPreviewEntity<IMember>(previewExists, content, xml));
        }

        protected override string GetDatabaseFieldNameForOrderBy(string orderBy)
        {
            //Some custom ones
            switch (orderBy.ToUpperInvariant())
            {
                case "EMAIL":
                    return "cmsMember.Email";
                case "LOGINNAME":
                    return "cmsMember.LoginName";
            }

            return base.GetDatabaseFieldNameForOrderBy(orderBy);
        }

        protected override string GetEntityPropertyNameForOrderBy(string orderBy)
        {
            //Some custom ones
            switch (orderBy.ToUpperInvariant())
            {
                case "LOGINNAME":
                    return "Username";
            }

            return base.GetEntityPropertyNameForOrderBy(orderBy);
        }

        private IEnumerable<IMember> ProcessQuery(Sql sql)
        {
            //NOTE: This doesn't allow properties to be part of the query
            var dtos = Database.Fetch<MemberDto, ContentVersionDto, ContentDto, NodeDto>(sql);

            //content types
            var contentTypes = _memberTypeRepository.GetAll(dtos.Select(x => x.ContentVersionDto.ContentDto.ContentTypeId).ToArray())
                .ToArray();

            //Go get the property data for each document
            var docDefs = dtos.Select(dto => new DocumentDefinition(
                dto.NodeId,
                dto.ContentVersionDto.VersionId,
                dto.ContentVersionDto.VersionDate,
                dto.ContentVersionDto.ContentDto.NodeDto.CreateDate,
                contentTypes.First(ct => ct.Id == dto.ContentVersionDto.ContentDto.ContentTypeId)))
                .ToArray();

            var propertyData = GetPropertyCollection(sql, docDefs);

            return dtos.Select(dto => CreateMemberFromDto(
                dto,
                contentTypes.First(ct => ct.Id == dto.ContentVersionDto.ContentDto.ContentTypeId),
                propertyData[dto.NodeId]));
        }

        /// <summary>
        /// Private method to create a member object from a MemberDto
        /// </summary>
        /// <param name="dto"></param>
        /// <param name="contentType"></param>
        /// <param name="propCollection"></param>
        /// <returns></returns>
        private IMember CreateMemberFromDto(MemberDto dto,
            IMemberType contentType,
            PropertyCollection propCollection)
        {
            var factory = new MemberFactory(contentType, NodeObjectTypeId, dto.ContentVersionDto.NodeId);
            var member = factory.BuildEntity(dto);

            member.Properties = propCollection;

            //on initial construction we don't want to have dirty properties tracked
            // http://issues.umbraco.org/issue/U4-1946
            ((Entity)member).ResetDirtyProperties(false);
            return member;
        }

        /// <summary>
        /// Private method to create a member object from a MemberDto
        /// </summary>
        /// <param name="dto"></param>
        /// <param name="versionId"></param>
        /// <param name="docSql"></param>
        /// <returns></returns>
        private IMember CreateMemberFromDto(MemberDto dto, Guid versionId, Sql docSql)
        {
            var memberType = _memberTypeRepository.Get(dto.ContentVersionDto.ContentDto.ContentTypeId);

            var factory = new MemberFactory(memberType, NodeObjectTypeId, dto.ContentVersionDto.NodeId);
            var member = factory.BuildEntity(dto);

            var docDef = new DocumentDefinition(dto.ContentVersionDto.NodeId, versionId, member.UpdateDate, member.CreateDate, memberType);

            var properties = GetPropertyCollection(docSql, docDef);

            member.Properties = properties[dto.ContentVersionDto.NodeId];

            //on initial construction we don't want to have dirty properties tracked
            // http://issues.umbraco.org/issue/U4-1946
            ((Entity)member).ResetDirtyProperties(false);
            return member;
        }

        //private IMember BuildFromDto(List<MemberReadOnlyDto> dtos, Sql docSql)
        //{
        //    if (dtos == null || dtos.Any() == false)
        //        return null;
        //    var dto = dtos.First();

        //    var memberTypes = new Dictionary<string, IMemberType>
        //                      {
        //                          {
        //                              dto.ContentTypeAlias,
        //                              _memberTypeRepository.Get(dto.ContentTypeId)
        //                          }
        //                      };

        //    var factory = new MemberReadOnlyFactory(memberTypes);
        //    var member = factory.BuildEntity(dto);

        //    var properties = GetPropertyCollection(docSql, new DocumentDefinition(dto.NodeId, dto.VersionId, dto.UpdateDate, dto.CreateDate, member.ContentType));

        //    member.Properties = properties[dto.NodeId];

        //    return member;
        //}

        //private IEnumerable<IMember> BuildFromDtos(List<MemberReadOnlyDto> dtos, Sql docSql)
        //{
        //    if (dtos == null || dtos.Any() == false)
        //        return Enumerable.Empty<IMember>();

        //    //We assume that there won't exist a lot of MemberTypes, so the following should be fairly fast
        //    var memberTypes = new Dictionary<string, IMemberType>();
            
        //    //TODO: We should do an SQL 'IN' here
        //    var memberTypeList = _memberTypeRepository.GetAll();
        //    memberTypeList.ForEach(x => memberTypes.Add(x.Alias, x));

        //    var entities = new List<IMember>();
        //    var factory = new MemberReadOnlyFactory(memberTypes);
        //    foreach (var dto in dtos)
        //    {
        //        var entity = factory.BuildEntity(dto);

        //        var properties = GetPropertyCollection(docSql,new DocumentDefinition(dto.NodeId, dto.VersionId, dto.UpdateDate, dto.CreateDate, entity.ContentType));

        //        entity.Properties = properties[dto.NodeId];
                
        //        entities.Add(entity);
        //    }
        //    return entities;
        //}
    }
}
