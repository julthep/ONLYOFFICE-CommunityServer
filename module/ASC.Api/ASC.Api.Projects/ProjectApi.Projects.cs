/*
 *
 * (c) Copyright Ascensio System Limited 2010-2021
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * http://www.apache.org/licenses/LICENSE-2.0
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
*/


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Security;
using System.Web;

using ASC.Api.Attributes;
using ASC.Api.Documents;
using ASC.Api.Exceptions;
using ASC.Api.Projects.Wrappers;
using ASC.Api.Utils;
using ASC.Common.Logging;
using ASC.Core.Tenants;
using ASC.Core.Users;
using ASC.CRM.Core.Entities;
using ASC.Files.Core;
using ASC.MessagingSystem;
using ASC.Projects.Core.Domain;
using ASC.Projects.Engine;
using ASC.Specific;
using ASC.Web.Projects.Classes;

using Newtonsoft.Json;

using Comment = ASC.Projects.Core.Domain.Comment;
using Task = ASC.Projects.Core.Domain.Task;

namespace ASC.Api.Projects
{
    public partial class ProjectApi
    {
        #region projects

        #region Read

        ///<summary>
        ///Returns a list of all the portal projects with the base information about them.
        ///</summary>
        ///<short>
        ///Get projects
        ///</short>
        ///<category>Projects</category>
        ///<returns>List of projects</returns>
        [Read("")]
        public IEnumerable<ProjectWrapper> GetAllProjects()
        {
            return EngineFactory.ProjectEngine.GetAll().Select(ProjectWrapperSelector).ToList();
        }

        ///<summary>
        ///Returns a list of all the projects in which the current user participates.
        ///</summary>
        ///<short>
        ///Get my projects
        ///</short>
        ///<category>Projects</category>
        ///<returns>List of projects</returns>
        [Read(@"@self")]
        public IEnumerable<ProjectWrapper> GetMyProjects()
        {
            return EngineFactory
                .ProjectEngine
                .GetByParticipant(CurrentUserId)
                .Select(ProjectWrapperSelector)
                .ToList();
        }

        ///<summary>
        ///Returns a list of all the projects which the current user follows.
        ///</summary>
        ///<short>
        ///Get my followed projects
        ///</short>
        ///<category>Projects</category>
        ///<returns>List of projects</returns>
        [Read(@"@follow")]
        public IEnumerable<ProjectWrapper> GetFollowProjects()
        {
            return EngineFactory
                .ProjectEngine
                .GetFollowing(CurrentUserId)
                .Select(ProjectWrapperSelector)
                .ToList();
        }

        ///<summary>
        ///Returns a list of all the projects with a status specified in the request.
        ///</summary>
        ///<short>
        ///Get projects by status
        ///</short>
        ///<category>Projects</category>
        ///<param name="status">Project status: "open"|"paused"|"closed"</param>
        ///<returns>List of projects</returns>
        [Read("{status:(open|paused|closed)}")]
        public IEnumerable<ProjectWrapper> GetProjects(ProjectStatus status)
        {
            return EngineFactory.ProjectEngine.GetAll(status, 0).Select(ProjectWrapperSelector).ToList();
        }

        ///<summary>
        ///Returns the detailed information about a project with ID specified in the request.
        ///</summary>
        ///<short>
        ///Get a project by ID
        ///</short>
        ///<category>Projects</category>
        ///<param name="id">Project ID</param>
        ///<returns>Project</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Read(@"{id:[0-9]+}")]
        public ProjectWrapperFull GetProject(int id)
        {
            var isFollow = EngineFactory.ProjectEngine.IsFollow(id, CurrentUserId);
            var tags = EngineFactory.TagEngine.GetProjectTags(id).Select(r => r.Value).ToList();
            return new ProjectWrapperFull(this, EngineFactory.ProjectEngine.GetFullProjectByID(id).NotFoundIfNull(), EngineFactory.FileEngine.GetRoot(id), isFollow, tags);
        }

        ///<summary>
        ///Returns a list of all the portal projects filtered by project title, status, participant ID and other parameters specified in the request.
        ///</summary>
        ///<short>
        ///Get filtered projects
        ///</short>
        ///<category>Projects</category>
        ///<param name="tag" optional="true">Project tag</param>
        ///<param name="status" optional="true">Project status: "open"|"paused"|"closed"</param>
        ///<param name="participant" optional="true">Project participant GUID</param>
        ///<param name="manager" optional="true">Project manager GUID</param>
        ///<param name="departament">Project department</param>
        ///<param name="follow" optional="true">Specifies if the current user follows this project or not</param>
        ///<returns>List of projects</returns>
        [Read(@"filter")]
        public IEnumerable<ProjectWrapperFull> GetProjectsByFilter(int tag, ProjectStatus? status, Guid participant,
            Guid manager, Guid departament, bool follow)
        {
            var projectEngine = EngineFactory.ProjectEngine;

            var filter = CreateFilter(EntityType.Project);
            filter.ParticipantId = participant;
            filter.UserId = manager;
            filter.TagId = tag;
            filter.Follow = follow;
            filter.DepartmentId = departament;

            if (status != null)
                filter.ProjectStatuses.Add((ProjectStatus)status);

            SetTotalCount(projectEngine.GetByFilterCount(filter));

            var projects = projectEngine.GetByFilter(filter).NotFoundIfNull();
            var projectIds = projects.Select(p => p.ID).ToList();
            var projectRoots = EngineFactory.FileEngine.GetRoots(projectIds).ToList();
            ProjectSecurity.GetProjectSecurityInfo(projects);

            return projects.Select((t, i) => ProjectWrapperFullSelector(t, projectRoots[i])).ToList();
        }

        ///<summary>
        ///Returns the search results for a project containing the words/phrases matching the query specified in the request.
        ///</summary>
        ///<short>
        ///Search in a project
        ///</short>
        ///<category>Projects</category>
        ///<param name="id">Project ID</param>
        ///<param name="query">Search query</param>
        ///<returns>List of results</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Read(@"{id:[0-9]+}/@search/{query}")]
        public IEnumerable<SearchWrapper> SearchProject(int id, string query)
        {
            if (!EngineFactory.ProjectEngine.IsExists(id)) throw new ItemNotFoundException();
            return EngineFactory.SearchEngine.Search(query, id).Select(x => new SearchWrapper(x));
        }

        ///<summary>
        ///Returns a list of all the projects matching the query specified in the request.
        ///</summary>
        ///<short>
        ///Search projects
        ///</short>
        ///<category>Projects</category>
        ///<param name="query">Search query</param>
        ///<returns>List of results</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Read(@"@search/{query}")]
        public IEnumerable<SearchWrapper> SearchProjects(string query)
        {
            return EngineFactory.SearchEngine.Search(query).Select(x => new SearchWrapper(x));
        }

        #endregion

        #region Create

        /// <summary>
        /// Creates a new project using all the necessary (title, description, responsible ID, etc) and some optional parameters specified in the request.
        /// </summary>
        /// <short>
        /// Create a project
        /// </short>
        ///  <category>Projects</category>
        /// <param name="title">Project title</param>
        /// <param name="description">Project description</param>
        /// <param name="responsibleId">Project responsible ID</param>
        /// <param name="tags">Project tags</param>
        /// <param name="private">Private project or not</param>
        /// <param name="participants" optional="true">Project participants</param>
        /// <param name="notify" optional="true">Notifies a project manager about the project actions or not</param>
        /// <param name="tasks">Project tasks</param>
        /// <param name="milestones">Project milestones</param>
        /// <param name="notifyResponsibles">Notifies responsibles about the project actions or not</param>
        /// <returns>Newly created project</returns>
        /// <exception cref="ArgumentException"></exception>
        [Create("")]
        public ProjectWrapperFull CreateProject(string title,
            string description,
            Guid responsibleId,
            string tags,
            bool @private,
            IEnumerable<Guid> participants,
            bool? notify,
            IEnumerable<Task> tasks,
            IEnumerable<Milestone> milestones,
            bool? notifyResponsibles)
        {
            if (responsibleId == Guid.Empty) throw new ArgumentException(@"responsible can't be empty", "responsibleId");
            if (string.IsNullOrEmpty(title)) throw new ArgumentException(@"title can't be empty", "title");

            if (@private && ProjectSecurity.IsPrivateDisabled) throw new ArgumentException(@"private", "private");

            ProjectSecurity.DemandCreate<Project>(null);

            var projectEngine = EngineFactory.ProjectEngine;
            var participantEngine = EngineFactory.ParticipantEngine;
            var taskEngine = EngineFactory.TaskEngine;
            var milestoneEngine = EngineFactory.MilestoneEngine;

            var project = new Project
            {
                Title = title,
                Status = ProjectStatus.Open,
                Responsible = responsibleId,
                Description = description,
                Private = @private
            };

            //hack: fix bug 37888
            if (!ProjectSecurity.IsAdministrator())
            {
                project.Responsible = Core.SecurityContext.CurrentAccount.ID;
            }

            projectEngine.SaveOrUpdate(project, notify ?? true);
            projectEngine.AddToTeam(project, participantEngine.GetByID(responsibleId), notify ?? true);
            EngineFactory.TagEngine.SetProjectTags(project.ID, tags);


            var participantsList = participants.ToList();
            foreach (var participant in participantsList)
            {
                projectEngine.AddToTeam(project, participantEngine.GetByID(participant), notifyResponsibles ?? false);
            }

            foreach (var milestone in milestones)
            {
                milestone.Description = string.Empty;
                milestone.Project = project;
                milestoneEngine.SaveOrUpdate(milestone, notifyResponsibles ?? false);
            }
            var ml = milestones.ToArray();
            foreach (var task in tasks)
            {
                task.Description = string.Empty;
                task.Project = project;
                task.Status = TaskStatus.Open;
                if (task.Milestone != 0)
                {
                    task.Milestone = ml[task.Milestone - 1].ID;
                }
                taskEngine.SaveOrUpdate(task, null, notifyResponsibles ?? false);
            }

            //hack: fix bug 37888
            if (!ProjectSecurity.IsAdministrator())
            {
                project.Responsible = responsibleId;
                projectEngine.SaveOrUpdate(project, notify ?? true);
            }

            if (tasks.Any() || milestones.Any())
            {
                var order = JsonConvert.SerializeObject(
                        new
                        {
                            tasks = tasks.Select(r => r.ID).ToArray(),
                            milestones = milestones.Select(r => r.ID).ToArray()
                        });

                projectEngine.SetTaskOrder(project, order);
            }

            MessageService.Send(Request, MessageAction.ProjectCreated, MessageTarget.Create(project.ID), project.Title);

            return new ProjectWrapperFull(this, project, EngineFactory.FileEngine.GetRoot(project.ID)) { ParticipantCount = participantsList.Count() + 1 };
        }

        /// <summary>
        /// Creates a new project with team security using all the necessary (title, description, responsible ID, etc) and some optional parameters specified in the request.
        /// </summary>
        /// <short>
        /// Create a project with team security
        /// </short>
        ///  <category>Projects</category>
        /// <param name="title">Project title</param>
        /// <param name="description">Project description</param>
        /// <param name="responsibleId">Project responsible ID</param>
        /// <param name="tags">Project tags</param>
        /// <param name="private">Private project or not</param>
        /// <param name="participants" optional="true">Project participants</param>
        /// <param name="notify" optional="true">Notifies a project manager about the project actions or not</param>
        /// <param name="tasks">Project tasks</param>
        /// <param name="milestones">Project milestones</param>
        /// <param name="notifyResponsibles">Notifies responsibles about the project actions or not</param>
        /// <returns>Newly created project</returns>
        [Create("withSecurity")]
        public ProjectWrapperFull CreateProject(string title,
            string description,
            Guid responsibleId,
            string tags,
            bool @private,
            IEnumerable<Participant> participants,
            bool? notify,
            IEnumerable<Task> tasks,
            IEnumerable<Milestone> milestones,
            bool? notifyResponsibles)
        {
            var project = CreateProject(title, description, responsibleId, tags, @private,
                participants.Select(r => r.ID).ToList(), notify, tasks, milestones, notifyResponsibles);

            foreach (var participant in participants.Where(r => !ProjectSecurity.IsAdministrator(r.ID)))
            {
                EngineFactory.ProjectEngine.SetTeamSecurity(project.Id, participant);
            }

            return project;
        }

        #endregion

        #region Update

        ///<summary>
        ///Updates the existing project using all the parameters (project ID, title, description, responsible ID, etc) specified in the request.
        ///</summary>
        ///<short>
        ///Update a project
        ///</short>
        ///<category>Projects</category>
        ///<param name="id">Project ID</param>
        ///<param name="title">New project title</param>
        ///<param name="description">New project description</param>
        ///<param name="responsibleId">New project responsible ID</param>
        ///<param name="tags">New project tags</param>
        ///<param name="participants">New project participants</param>
        ///<param name="status">New project status: Open, Paused or Closed</param>
        ///<param name="private">Private project or not</param>
        ///<param name="notify">Notifies a project manager about the project actions or not</param>
        ///<returns>Updated project</returns>
        ///<exception cref="ArgumentException"></exception>
        ///<exception cref="ItemNotFoundException"></exception>
        [Update(@"{id:[0-9]+}")]
        public ProjectWrapperFull UpdateProject(int id, string title, string description, Guid responsibleId, string tags, IEnumerable<Guid> participants, ProjectStatus? status, bool? @private, bool notify)
        {
            if (responsibleId == Guid.Empty) throw new ArgumentException(@"responsible can't be empty", "responsibleId");
            if (string.IsNullOrEmpty(title)) throw new ArgumentException(@"title can't be empty", "title");

            var projectEngine = EngineFactory.ProjectEngine;
            var participantEngine = EngineFactory.ParticipantEngine;

            var project = projectEngine.GetByID(id).NotFoundIfNull();
            ProjectSecurity.DemandEdit(project);

            if (!projectEngine.IsInTeam(project.ID, responsibleId))
            {
                projectEngine.AddToTeam(project, participantEngine.GetByID(responsibleId), false);
            }

            project.Title = Update.IfNotEmptyAndNotEquals(project.Title, title);
            project.StatusChangedOn = TenantUtil.DateTimeNow();

            if (status.HasValue)
            {
                project.Status = status.Value;
            }

            project.Responsible = Update.IfNotEmptyAndNotEquals(project.Responsible, responsibleId);
            project.Description = Update.IfNotEmptyAndNotEquals(project.Description, description);

            if (@private.HasValue)
            {
                project.Private = @private.Value;
            }

            projectEngine.SaveOrUpdate(project, notify);
            if (tags != null)
            {
                EngineFactory.TagEngine.SetProjectTags(project.ID, tags);
            }
            projectEngine.UpdateTeam(project, participants, true);

            project.ParticipantCount = participants.Count();

            MessageService.Send(Request, MessageAction.ProjectUpdated, MessageTarget.Create(project.ID), project.Title);

            return ProjectWrapperFullSelector(project, EngineFactory.FileEngine.GetRoot(id));
        }

        ///<summary>
        ///Updates the existing project with team security using all the parameters (project ID, title, description, responsible ID, etc) specified in the request.
        ///</summary>
        ///<short>
        ///Update a project with team security
        ///</short>
        ///<category>Projects</category>
        ///<param name="id">Project ID</param>
        ///<param name="title">New project title</param>
        ///<param name="description">New project description</param>
        ///<param name="responsibleId">New project responsible ID</param>
        ///<param name="tags">New project tags</param>
        ///<param name="participants">New project participants</param>
        ///<param name="status">New project status: Open, Paused or Closed</param>
        ///<param name="private">Private project or not</param>
        ///<param name="notify">Notifies a project manager about the project actions or not</param>
        ///<returns>Updated project</returns>
        [Update(@"{id:[0-9]+}/withSecurityInfo")]
        public ProjectWrapperFull UpdateProject(int id, string title, string description, Guid responsibleId,
            string tags, IEnumerable<Participant> participants, ProjectStatus? status, bool? @private, bool notify)
        {
            var project = UpdateProject(id, title, description, responsibleId, tags,
                participants.Select(r => r.ID),
                status,
                @private,
                notify);

            foreach (var participant in participants)
            {
                EngineFactory.ProjectEngine.SetTeamSecurity(project.Id, participant);
            }

            return project;
        }

        ///<summary>
        ///Updates a status of a project with the ID specified in the request.
        ///</summary>
        ///<short>
        ///Update a project status
        ///</short>
        ///<category>Projects</category>
        ///<param name="id">Project ID</param>
        ///<param name="status">New project status: Open, Paused or Closed</param>
        ///<returns>Updated project</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Update(@"{id:[0-9]+}/status")]
        public ProjectWrapperFull UpdateProject(int id, ProjectStatus status)
        {
            var projectEngine = EngineFactory.ProjectEngine;
            var project = projectEngine.GetFullProjectByID(id).NotFoundIfNull();

            projectEngine.ChangeStatus(project, status);
            MessageService.Send(Request, MessageAction.ProjectUpdatedStatus, MessageTarget.Create(project.ID), project.Title, LocalizedEnumConverter.ConvertToString(project.Status));

            return ProjectWrapperFullSelector(project, EngineFactory.FileEngine.GetRoot(id));
        }

        #endregion

        #region Delete

        ///<summary>
        ///Deletes a project with the ID specified in the request from the portal.
        ///</summary>
        ///<short>
        ///Delete a project
        ///</short>
        ///<category>Projects</category>
        ///<param name="id">Project ID</param>
        ///<returns>Deleted project</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Delete(@"{id:[0-9]+}")]
        public ProjectWrapperFull DeleteProject(int id)
        {
            var projectEngine = EngineFactory.ProjectEngine;

            var project = projectEngine.GetByID(id).NotFoundIfNull();
            ProjectSecurity.DemandEdit(project);

            var folderId = EngineFactory.FileEngine.GetRoot(id);
            projectEngine.Delete(id);
            MessageService.Send(Request, MessageAction.ProjectDeleted, MessageTarget.Create(project.ID), project.Title);

            return ProjectWrapperFullSelector(project, folderId);
        }

        ///<summary>
        ///Deletes the projects with the IDs specified in the request from the portal.
        ///</summary>
        ///<short>
        ///Delete projects
        ///</short>
        ///<category>Projects</category>
        ///<param name="projectids">List of project IDs</param>
        ///<returns>Deleted projects</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Delete(@"")]
        public IEnumerable<ProjectWrapperFull> DeleteProjects(int[] projectids)
        {
            var result = new List<ProjectWrapperFull>(projectids.Length);

            foreach (var id in projectids)
            {
                try
                {
                    result.Add(DeleteProject(id));
                }
                catch (Exception e)
                {
                    LogManager.GetLogger("ASC").Error("DeleteProjects " + id, e);
                }
            }

            return result;
        }

        #endregion

        #region Follow, Tags, Time

        ///<summary>
        ///Subscribes to or unsubscribes from the notifications about the actions performed in the project with the ID specified in the request.
        ///</summary>
        ///<short>
        ///Project subscription
        ///</short>
        ///<category>Projects</category>
        ///<param name="projectId">Project ID</param>
        ///<returns>Project</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Update(@"{projectid:[0-9]+}/follow")]
        public ProjectWrapper FollowToProject(int projectId)
        {
            var projectEngine = EngineFactory.ProjectEngine;
            var project = projectEngine.GetByID(projectId).NotFoundIfNull();

            ProjectSecurity.DemandAuthentication();

            var participantEngine = EngineFactory.ParticipantEngine;
            if (participantEngine.GetFollowingProjects(CurrentUserId).Contains(projectId))
            {
                participantEngine.RemoveFromFollowingProjects(projectId, CurrentUserId);
                MessageService.Send(Request, MessageAction.ProjectUnfollowed, MessageTarget.Create(project.ID), project.Title);
            }
            else
            {
                participantEngine.AddToFollowingProjects(projectId, CurrentUserId);
                MessageService.Send(Request, MessageAction.ProjectFollowed, MessageTarget.Create(project.ID), project.Title);
            }

            return ProjectWrapperSelector(project);
        }

        ///<summary>
        ///Updates a tag for the selected project with a tag specified in the request.
        ///</summary>
        ///<short>
        ///Update a project tag
        ///</short>
        ///<category>Tags</category>
        ///<param name="id">Project ID</param>
        ///<param name="tags">New project tag</param>
        ///<returns>Project</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Update(@"{id:[0-9]+}/tag")]
        public ProjectWrapperFull UpdateProjectTags(int id, string tags)
        {
            var project = EngineFactory.ProjectEngine.GetByID(id).NotFoundIfNull();
            ProjectSecurity.DemandEdit(project);

            EngineFactory.TagEngine.SetProjectTags(id, tags);

            return ProjectWrapperFullSelector(project, EngineFactory.FileEngine.GetRoot(id));
        }

        ///<summary>
        ///Updates the tags for the selected project with the tags specified in the request.
        ///</summary>
        ///<short>
        ///Update project tags
        ///</short>
        ///<category>Tags</category>
        ///<param name="id">Project ID</param>
        ///<param name="tags">New project tags</param>
        ///<returns>Project</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Update(@"{id:[0-9]+}/tags")]
        public ProjectWrapperFull UpdateProjectTags(int id, IEnumerable<int> tags)
        {
            var project = EngineFactory.ProjectEngine.GetByID(id).NotFoundIfNull();
            ProjectSecurity.DemandEdit(project);

            EngineFactory.TagEngine.SetProjectTags(id, tags);

            return ProjectWrapperFullSelector(project, EngineFactory.FileEngine.GetRoot(id));
        }

        ///<summary>
        ///Returns the detailed information about the time spent on the project with the ID specified in the request.
        ///</summary>
        ///<short>
        ///Get project time
        ///</short>
        ///<category>Time</category>
        ///<param name="id">Project ID</param>
        ///<returns>List of project time</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Read(@"{id:[0-9]+}/time")]
        public IEnumerable<TimeWrapper> GetProjectTime(int id)
        {
            if (!EngineFactory.ProjectEngine.IsExists(id)) throw new ItemNotFoundException();
            return EngineFactory.TimeTrackingEngine.GetByProject(id).Select(TimeWrapperSelector);
        }

        ///<summary>
        ///Returns the total time spent on the project with the ID specified in the request.
        ///</summary>
        ///<short>Get total project time</short>
        ///<category>Time</category>
        ///<param name="id">Project ID</param>
        ///<returns>Project time</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Read(@"{id:[0-9]+}/time/total")]
        public string GetTotalProjectTime(int id)
        {
            if (!EngineFactory.ProjectEngine.IsExists(id)) throw new ItemNotFoundException();
            return EngineFactory.TimeTrackingEngine.GetTotalByProject(id);
        }

        #endregion

        #region Milestones

        ///<summary>
        ///Adds a new milestone using the parameters (project ID, milestone title, deadline, etc) specified in the request.
        ///</summary>
        ///<short>
        ///Add a milestone
        ///</short>
        ///<category>Milestones</category>
        ///<param name="id">Project ID</param>
        ///<param name="title">Milestone title</param>
        ///<param name="deadline">Milestone deadline</param>
        ///<param name="isKey">Specifies if this is a key milestone or not</param>
        ///<param name="isNotify">Reminds me 48 hours before the milestone due date</param>
        ///<param name="description">Milestone description</param>
        ///<param name="responsible">Milestone responsible</param>
        ///<param name="notifyResponsible">Notifies the responsible about the milestone actions or not</param>
        ///<returns>Added milestone</returns>
        ///<exception cref="ArgumentNullException"></exception>
        ///<exception cref="ItemNotFoundException"></exception>
        [Create(@"{id:[0-9]+}/milestone")]
        public MilestoneWrapper AddProjectMilestone(int id, string title, ApiDateTime deadline, bool isKey, bool isNotify, string description, Guid responsible, bool notifyResponsible)
        {
            if (title == null) throw new ArgumentNullException("title");
            if (deadline == DateTime.MinValue) throw new ArgumentNullException("deadline");

            var project = EngineFactory.ProjectEngine.GetByID(id).NotFoundIfNull();
            ProjectSecurity.DemandCreate<Milestone>(project);

            var milestone = new Milestone
            {
                Description = description ?? "",
                Project = project,
                Title = title.Trim(),
                DeadLine = deadline,
                IsKey = isKey,
                Status = MilestoneStatus.Open,
                IsNotify = isNotify,
                Responsible = responsible
            };
            EngineFactory.MilestoneEngine.SaveOrUpdate(milestone, notifyResponsible);
            MessageService.Send(Request, MessageAction.MilestoneCreated, MessageTarget.Create(milestone.ID), milestone.Project.Title, milestone.Title);

            return MilestoneWrapperSelector(milestone);
        }

        ///<summary>
        ///Returns a list of all the milestones within a project with the ID specified in the request.
        ///</summary>
        ///<short>
        ///Get milestones by project ID
        ///</short>
        ///<category>Milestones</category>
        ///<param name="id">Project ID</param>
        ///<returns>List of milestones</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Read(@"{id:[0-9]+}/milestone")]
        public IEnumerable<MilestoneWrapper> GetProjectMilestones(int id)
        {
            var project = EngineFactory.ProjectEngine.GetByID(id).NotFoundIfNull();

            //NOTE: move to engine
            if (!ProjectSecurity.CanRead<Milestone>(project)) throw ProjectSecurity.CreateSecurityException();

            var milestones = EngineFactory.MilestoneEngine.GetByProject(id);

            return milestones.Select(MilestoneWrapperSelector);
        }

        ///<summary>
        ///Returns a list of all the milestones with the selected status within a project with the ID specified in the request.
        ///</summary>
        ///<short>
        ///Get milestones by project ID and milestone status
        ///</short>
        ///<category>Milestones</category>
        ///<param name="id">Project ID</param>
        ///<param name="status">Milestone status</param>
        ///<returns>List of milestones</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Read(@"{id:[0-9]+}/milestone/{status:(open|closed|late|disable)}")]
        public IEnumerable<MilestoneWrapper> GetProjectMilestones(int id, MilestoneStatus status)
        {
            var project = EngineFactory.ProjectEngine.GetByID(id).NotFoundIfNull();

            if (!ProjectSecurity.CanRead<Milestone>(project)) throw ProjectSecurity.CreateSecurityException();

            var milestones = EngineFactory.MilestoneEngine.GetByStatus(id, status);

            return milestones.Select(MilestoneWrapperSelector);
        }

        #endregion

        #region Team

        ///<summary>
        ///Returns a list of all the users participating in the project with the ID specified in the request.
        ///</summary>
        ///<short>
        ///Get a project team
        ///</short>
        ///<category>Teams</category>
        ///<param name="projectid">Project ID</param>
        ///<returns>List of team members</returns>
        [Read(@"{projectid:[0-9]+}/team")]
        public IEnumerable<ParticipantWrapper> GetProjectTeam(int projectid)
        {
            var projectEngine = EngineFactory.ProjectEngine;
            if (!projectEngine.IsExists(projectid)) throw new ItemNotFoundException();

            return projectEngine.GetTeam(projectid)
                                .Select(x => new ParticipantWrapper(this, x))
                                .OrderBy(r => r.DisplayName).ToList();
        }

        ///<summary>
        ///Returns a list of all the current and excluded project team members.
        ///</summary>
        ///<short>
        ///Get a project team with excluded members
        ///</short>
        ///<category>Teams</category>
        ///<param name="projectid">Project ID</param>
        ///<returns>List of team members</returns>
        [Read(@"{projectid:[0-9]+}/teamExcluded")]
        public IEnumerable<ParticipantWrapper> GetProjectTeamExcluded(int projectid)
        {
            var projectEngine = EngineFactory.ProjectEngine;
            if (!projectEngine.IsExists(projectid)) throw new ItemNotFoundException();

            return projectEngine.GetProjectTeamExcluded(projectid)
                                .Select(x => new ParticipantWrapper(this, x))
                                .OrderBy(r => r.DisplayName).ToList();
        }

        ///<summary>
        ///Returns a list of all the users participating in the projects with the ID specified in the request.
        ///</summary>
        ///<short>
        ///Get team members of projects
        ///</short>
        ///<category>Teams</category>
        ///<param name="ids">List of project IDs</param>
        ///<returns>List of team members</returns>
        [Create(@"team")]
        public IEnumerable<ParticipantWrapper> GetProjectTeam(List<int> ids)
        {
            return EngineFactory.ProjectEngine.GetTeam(ids)
                                .Select(x => new ParticipantWrapper(this, x))
                                .OrderBy(r => r.DisplayName).ToList();
        }

        ///<summary>
        ///Adds a user with the ID specified in the request to the selected project team.
        ///</summary>
        ///<short>
        ///Add a user to the team
        ///</short>
        ///<category>Teams</category>
        ///<param name="projectid">Project ID</param>
        ///<param name="userId">User ID</param>
        ///<returns>List of team members</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Create(@"{projectid:[0-9]+}/team")]
        public IEnumerable<ParticipantWrapper> AddToProjectTeam(int projectid, Guid userId)
        {
            var projectEngine = EngineFactory.ProjectEngine;

            var project = projectEngine.GetByID(projectid).NotFoundIfNull();
            ProjectSecurity.DemandEditTeam(project);

            projectEngine.AddToTeam(project, EngineFactory.ParticipantEngine.GetByID(userId), true);

            return GetProjectTeam(projectid);
        }

        ///<summary>
        ///Sets the security rights to the user with the ID specified in the request within the selected project.
        ///</summary>
        ///<short>
        ///Set team security
        ///</short>
        ///<category>Teams</category>
        ///<param name="projectid">Project ID</param>
        ///<param name="userId">User ID</param>
        ///<param name="security">Security rights</param>
        ///<param name="visible">Specifies if the security rights for the user will be visible or not</param>
        ///<returns>List of team members</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Update(@"{projectid:[0-9]+}/team/security")]
        public IEnumerable<ParticipantWrapper> SetProjectTeamSecurity(int projectid, Guid userId, ProjectTeamSecurity security, bool visible)
        {
            var projectEngine = EngineFactory.ProjectEngine;

            var project = projectEngine.GetByID(projectid).NotFoundIfNull();
            ProjectSecurity.DemandEditTeam(project);

            if (!projectEngine.IsInTeam(projectid, userId))
            {
                throw new ArgumentOutOfRangeException("userId", "Not a project memeber");
            }

            projectEngine.SetTeamSecurity(project, EngineFactory.ParticipantEngine.GetByID(userId), security, visible);

            var team = GetProjectTeam(projectid);
            var user = team.SingleOrDefault(t => t.Id == userId);
            if (user != null)
            {
                MessageService.Send(Request, MessageAction.ProjectUpdatedMemberRights, MessageTarget.Create(project.ID), project.Title, HttpUtility.HtmlDecode(user.DisplayName));
            }

            return team;
        }

        ///<summary>
        ///Removes a user with the ID specified in the request from the selected project team.
        ///</summary>
        ///<short>
        ///Remove a user from the team
        ///</short>
        ///<category>Teams</category>
        ///<param name="projectid">Project ID</param>
        ///<param name="userId">User ID</param>
        ///<returns>List of team members</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Delete(@"{projectid:[0-9]+}/team")]
        public IEnumerable<ParticipantWrapper> DeleteFromProjectTeam(int projectid, Guid userId)
        {
            var projectEngine = EngineFactory.ProjectEngine;

            var project = projectEngine.GetByID(projectid).NotFoundIfNull();
            ProjectSecurity.DemandEditTeam(project);

            var particapant = EngineFactory.ParticipantEngine.GetByID(userId);
            projectEngine.RemoveFromTeam(project, particapant, true);

            MessageService.Send(Request, MessageAction.ProjectDeletedMember, MessageTarget.Create(project.ID), project.Title, particapant.UserInfo.DisplayUserName(false));

            return GetProjectTeam(projectid);
        }

        ///<summary>
        ///Updates a project team with the user IDs specified in the request.
        ///</summary>
        ///<short>
        ///Update a project team
        ///</short>
        ///<category>Teams</category>
        ///<param name="projectId">Project ID</param>
        ///<param name="participants">List of user IDs</param>
        ///<param name="notify">Notifies a project team about the updated project or not</param>
        ///<returns>Number of project participants</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Update(@"{projectid:[0-9]+}/team")]
        public IEnumerable<ParticipantWrapper> UpdateProjectTeam(int projectId, IEnumerable<Guid> participants, bool notify)
        {
            var projectEngine = EngineFactory.ProjectEngine;

            var project = projectEngine.GetByID(projectId).NotFoundIfNull();
            ProjectSecurity.DemandEditTeam(project);

            var participantsList = participants.ToList();
            projectEngine.UpdateTeam(project, participantsList, notify);

            var team = GetProjectTeam(projectId);
            MessageService.Send(Request, MessageAction.ProjectUpdatedTeam, MessageTarget.Create(project.ID), project.Title, team.Select(t => t.DisplayName));

            return team;
        }

        #endregion

        #region Task

        ///<summary>
        ///Returns a list of all the tasks within a project with the ID specified in the request.
        ///</summary>
        ///<short>
        ///Get tasks
        ///</short>
        ///<category>Tasks</category>
        ///<param name="projectid">Project ID</param>
        ///<returns>List of tasks</returns>
        ///<exception cref="ItemNotFoundException">List of tasks</exception>
        [Read(@"{projectid:[0-9]+}/task")]
        public IEnumerable<TaskWrapper> GetProjectTasks(int projectid)
        {
            if (!EngineFactory.ProjectEngine.IsExists(projectid)) throw new ItemNotFoundException();

            return EngineFactory
                .TaskEngine.GetByProject(projectid, TaskStatus.Open, Guid.Empty)
                .Select(TaskWrapperSelector)
                .ToList();
        }

        ///<summary>
        ///Adds a task to the selected project with the parameters (responsible user ID, task description, deadline time, etc) specified in the request.
        ///</summary>
        ///<short>
        ///Add a task
        ///</short>
        ///<category>Tasks</category>
        ///<param name="projectid">Project ID</param>
        ///<param name="description">Task description</param>
        ///<param name="deadline">Task deadline time</param>
        ///<param name="priority">Task priority: Low|Normal|High</param>
        ///<param name="title">Task title</param>
        ///<param name="milestoneid">Task milestone ID</param>
        ///<param name="responsibles">List of responsibles</param>
        ///<param name="notify">Notifies the responsibles about the task actions or not</param>
        ///<param name="startDate">Task start date</param>
        ///<returns>Added task</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Create(@"{projectid:[0-9]+}/task")]
        public TaskWrapper AddProjectTask(int projectid, string description, ApiDateTime deadline,
                                          TaskPriority priority, string title, int milestoneid,
                                          IEnumerable<Guid> responsibles, bool notify, ApiDateTime startDate)
        {
            if (string.IsNullOrEmpty(title)) throw new ArgumentException(@"title can't be empty", "title");

            var projectEngine = EngineFactory.ProjectEngine;

            var project = projectEngine.GetByID(projectid).NotFoundIfNull();


            if (milestoneid > 0 && !EngineFactory.MilestoneEngine.IsExists(milestoneid))
            {
                throw new ItemNotFoundException("Milestone not found");
            }

            var task = new Task
            {
                CreateBy = CurrentUserId,
                CreateOn = TenantUtil.DateTimeNow(),
                Deadline = deadline,
                Description = description ?? "",
                Priority = priority,
                Status = TaskStatus.Open,
                Title = title,
                Project = project,
                Milestone = milestoneid,
                Responsibles = new List<Guid>(responsibles.Distinct()),
                StartDate = startDate
            };
            EngineFactory.TaskEngine.SaveOrUpdate(task, null, notify);

            MessageService.Send(Request, MessageAction.TaskCreated, MessageTarget.Create(task.ID), project.Title, task.Title);

            return GetTask(task);
        }

        ///<summary>
        ///Adds a task to the selected project by message ID specified in the request.
        ///</summary>
        ///<short>
        ///Add a task by message ID
        ///</short>
        ///<category>Tasks</category>
        ///<param name="projectid">Project ID</param>
        ///<param name="messageid">Message ID</param>
        ///<returns>Added task</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Create(@"{projectid:[0-9]+}/task/{messageid:[0-9]+}")]
        public TaskWrapper AddProjectTaskByMessage(int projectid, int messageid)
        {
            var projectEngine = EngineFactory.ProjectEngine;
            var messageEngine = EngineFactory.MessageEngine;
            var taskEngine = EngineFactory.TaskEngine;
            var commentEngine = EngineFactory.CommentEngine;

            var project = projectEngine.GetByID(projectid).NotFoundIfNull();
            var discussion = messageEngine.GetByID(messageid).NotFoundIfNull();

            ProjectSecurity.DemandCreate<Task>(project);

            var task = new Task
            {
                CreateBy = CurrentUserId,
                CreateOn = TenantUtil.DateTimeNow(),
                Status = TaskStatus.Open,
                Title = discussion.Title,
                Project = project
            };

            taskEngine.SaveOrUpdate(task, null, true);

            commentEngine.SaveOrUpdate(new Comment
            {
                OldGuidId = Guid.NewGuid(),
                TargetUniqID = ProjectEntity.BuildUniqId<Task>(task.ID),
                Content = discussion.Description
            });
            //copy comments
            var comments = commentEngine.GetComments(discussion);
            var newOldComments = new Dictionary<Guid, Guid>();

            var i = 1;
            foreach (var comment in comments)
            {
                var newID = Guid.NewGuid();
                newOldComments.Add(comment.OldGuidId, newID);

                comment.OldGuidId = newID;
                comment.CreateOn = TenantUtil.DateTimeNow().AddSeconds(i);
                comment.TargetUniqID = ProjectEntity.BuildUniqId<Task>(task.ID);

                if (!comment.Parent.Equals(Guid.Empty))
                {
                    comment.Parent = newOldComments[comment.Parent];
                }

                commentEngine.SaveOrUpdate(comment);
                i++;
            }

            //copy files
            var files = messageEngine.GetFiles(discussion);

            foreach (var file in files)
            {
                taskEngine.AttachFile(task, file.ID);
            }

            //copy recipients

            foreach (var participiant in messageEngine.GetSubscribers(discussion))
            {
                taskEngine.Subscribe(task, new Guid(participiant.ID));
            }

            MessageService.Send(Request, MessageAction.TaskCreatedFromDiscussion, MessageTarget.Create(task.ID), project.Title, discussion.Title, task.Title);

            return TaskWrapperSelector(task);
        }

        ///<summary>
        ///Returns a list of all the tasks with the selected status in the project with the ID specified in the request.
        ///</summary>
        ///<short>
        ///Get tasks by status
        ///</short>
        ///<category>Tasks</category>
        ///<param name="projectid">Project ID</param>
        ///<param name="status">Task status: not accept|open|closed|disable|unclassified|not in milestone</param>
        ///<returns>List of tasks</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Read(@"{projectid:[0-9]+}/task/{status:(notaccept|open|closed|disable|unclassified|notinmilestone)}")]
        public IEnumerable<TaskWrapper> GetProjectTasks(int projectid, TaskStatus status)
        {
            if (!EngineFactory.ProjectEngine.IsExists(projectid)) throw new ItemNotFoundException();
            return EngineFactory
                .TaskEngine.GetByProject(projectid, status, Guid.Empty)
                .Select(TaskWrapperSelector).ToList();
        }

        ///<summary>
        ///Returns a list of all the tasks for the current user with the selected status in the project with the ID specified in the request.
        ///</summary>
        ///<short>
        ///Get my tasks by status and project ID
        ///</short>
        ///<category>Tasks</category>
        ///<param name="projectid">Project ID</param>
        ///<param name="status">Task status: not accept|open|closed|disable|unclassified|not in milestone</param>
        ///<returns>List of tasks</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Read(@"{projectid:[0-9]+}/task/@self/{status:(notaccept|open|closed|disable|unclassified|notinmilestone)}")]
        public IEnumerable<TaskWrapper> GetProjectMyTasks(int projectid, TaskStatus status)
        {
            if (!EngineFactory.ProjectEngine.IsExists(projectid)) throw new ItemNotFoundException();

            return EngineFactory
                .TaskEngine.GetByProject(projectid, status, CurrentUserId)
                .Select(TaskWrapperSelector)
                .ToList();
        }

        #endregion

        #region Files

        ///<summary>
        ///Returns the detailed list of all the files and folders for the project with the ID specified in the request.
        ///</summary>
        ///<short>
        ///Get project files by project ID
        ///</short>
        ///<category>Files</category>
        ///<param name="id">Project ID</param>
        ///<returns>Project files</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Read(@"{id:[0-9]+}/files")]
        public FolderContentWrapper GetProjectFiles(int id)
        {
            var project = EngineFactory.ProjectEngine.GetByID(id).NotFoundIfNull();

            if (ProjectSecurity.CanReadFiles(project))
                return documentsApi.GetFolder(EngineFactory.FileEngine.GetRoot(id).ToString(), Guid.Empty, FilterType.None);

            throw new SecurityException("Access to files is denied");
        }

        ///<summary>
        ///Returns a list of all the files within the entity (message or task) with the type and ID specified in the request.
        ///</summary>
        ///<short>
        ///Get entity files
        ///</short>
        ///<category>Files</category>
        ///<param name="entityType">Entity type</param>
        ///<param name="entityID">Entity ID</param>
        ///<returns>Files</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Read(@"{entityID:[0-9]+}/entityfiles")]
        public IEnumerable<FileWrapper> GetEntityFiles(EntityType entityType, int entityID)
        {
            switch (entityType)
            {
                case EntityType.Message:
                    return GetMessageFiles(entityID);

                case EntityType.Task:
                    return GetTaskFiles(entityID);
            }

            return new List<FileWrapper>();
        }

        ///<summary>
        ///Uploads the selected files to the entity (project, milestone, task) with the type and ID specified in the request.
        ///</summary>
        ///<short>
        ///Upload files to the entity
        ///</short>
        ///<category>Files</category>
        ///<param name="entityType">Entity type </param>
        ///<param name="entityID">Entity ID</param>
        ///<param name="files">List of file IDs</param>
        ///<returns>Uploaded files</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        ///<visible>false</visible>
        [Create(@"{entityID:[0-9]+}/entityfiles")]
        public IEnumerable<FileWrapper> UploadFilesToEntity(EntityType entityType, int entityID, IEnumerable<int> files)
        {
            var fileEngine = EngineFactory.FileEngine;
            var filesList = files.ToList();

            switch (entityType)
            {
                case EntityType.Message:
                    UploadFilesToMessage(entityID, filesList);
                    break;

                case EntityType.Task:
                    UploadFilesToTask(entityID, filesList);
                    break;
            }

            var listFiles = filesList.Select(r => fileEngine.GetFile(r).NotFoundIfNull()).ToList();

            return listFiles.Select(FileWrapperSelector);
        }

        ///<summary>
        ///Detaches the selected file from the entity (project, milestone, task) with the type and ID specified in the request.
        ///</summary>
        ///<short>
        ///Detach a file from the entity
        ///</short>
        ///<category>Files</category>
        ///<param name="entityType">Entity type</param>
        ///<param name="entityID">Entity ID</param>
        ///<param name="fileid">File ID</param>
        ///<returns>Detached file</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        ///<visible>false</visible>
        [Delete(@"{entityID:[0-9]+}/entityfiles")]
        public FileWrapper DetachFileFromEntity(EntityType entityType, int entityID, int fileid)
        {
            switch (entityType)
            {
                case EntityType.Message:
                    DetachFileFromMessage(entityID, fileid);
                    break;

                case EntityType.Task:
                    DetachFileFromTask(entityID, fileid);
                    break;
            }

            var file = EngineFactory.FileEngine.GetFile(fileid).NotFoundIfNull();
            return FileWrapperSelector(file);
        }

        ///<summary>
        ///Detaches the selected files from the entity (project, milestone, task) with the type and ID specified in the request.
        ///</summary>
        ///<short>
        ///Detach files from the entity
        ///</short>
        ///<category>Files</category>
        ///<param name="entityType">Entity type</param>
        ///<param name="entityID">Entity ID</param>
        ///<param name="files">List of file IDs</param>
        ///<returns>Detached files</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        ///<visible>false</visible>
        [Delete(@"{entityID:[0-9]+}/entityfilesmany")]
        public IEnumerable<FileWrapper> DetachFileFromEntity(EntityType entityType, int entityID, IEnumerable<int> files)
        {
            var fileEngine = EngineFactory.FileEngine;
            var filesList = files.ToList();

            switch (entityType)
            {
                case EntityType.Message:
                    DetachFileFromMessage(entityID, filesList);
                    break;

                case EntityType.Task:
                    DetachFileFromTask(entityID, filesList);
                    break;
            }

            var listFiles = filesList.Select(r => fileEngine.GetFile(r).NotFoundIfNull()).ToList();

            return listFiles.Select(FileWrapperSelector);
        }

        ///<summary>
        ///Uploads the selected files to the entity (project, milestone, task) with the type and ID specified in the request.
        ///</summary>
        ///<short>
        ///Upload files to the entity
        ///</short>
        ///<category>Files</category>
        ///<param name="entityType">Entity type</param>
        ///<param name="entityID">Entity ID</param>
        ///<param name="folderid">Folder ID</param>
        ///<param name="file" visible="false">Request input stream</param>
        ///<param name="contentType" visible="false">Content-Type header</param>
        ///<param name="contentDisposition" visible="false">Content-Disposition header</param>
        ///<param name="files" visible="false">List of files when posted as multipart/form-data</param>
        ///<param name="createNewIfExist" visible="false">Creates a new file if it already exists</param>
        ///<param name="storeOriginalFileFlag" visible="false">If true, then upload files in the original formats as well</param>
        ///<returns>Uploaded files</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        ///<visible>false</visible>
        [Create(@"{entityID:[0-9]+}/entityfiles/upload")]
        public object UploadFilesToEntity(EntityType entityType, int entityID, string folderid, Stream file, ContentType contentType, ContentDisposition contentDisposition, IEnumerable<HttpPostedFileBase> files, bool createNewIfExist, bool storeOriginalFileFlag)
        {
            if (!files.Any()) return new object();
            var fileWrappers = documentsApi.UploadFile(folderid, file, contentType, contentDisposition, files, createNewIfExist, storeOriginalFileFlag);

            if (fileWrappers == null) return null;

            var fileIDs = new List<int>();

            var wrappers = fileWrappers as IEnumerable<FileWrapper>;
            if (wrappers != null)
            {
                fileIDs.AddRange(wrappers.Select(r => (int)r.Id));
            }

            var fileWrapper = fileWrappers as FileWrapper;
            if (fileWrapper != null)
            {
                fileIDs.Add((int)fileWrapper.Id);
            }

            switch (entityType)
            {
                case EntityType.Message:
                    UploadFilesToMessage(entityID, fileIDs);
                    break;

                case EntityType.Task:
                    UploadFilesToTask(entityID, fileIDs);
                    break;
            }

            return fileWrappers;
        }

        #endregion

        #endregion

        #region contacts

        ///<summary>
        /// Returns a list of all the projects linked with a contact with the ID specified in the request.
        ///</summary>
        ///<param name="contactid">Contact ID</param>
        ///<category>Contacts</category>
        ///<short>Get contact projects</short> 
        ///<returns>
        ///List of projects
        ///</returns>
        ///<exception cref="ArgumentException"></exception>
        [Read("contact/{contactid:[0-9]+}")]
        public IEnumerable<ProjectWrapperFull> GetProjectsByContactID(int contactid)
        {
            if (contactid <= 0) throw new ArgumentException();

            return EngineFactory.ProjectEngine.GetProjectsByContactID(contactid)
                .Select(x => ProjectWrapperFullSelector(x, EngineFactory.FileEngine.GetRoot(x.ID))).ToList();
        }

        ///<summary>
        /// Adds the selected contact to the project with the ID specified in the request.
        ///</summary>
        ///<param name="projectid">Project ID</param>
        ///<param name="contactid">Contact ID</param>
        ///<category>Contacts</category>
        ///<short>Add a project contact</short> 
        ///<returns>Project</returns>
        ///<exception cref="ArgumentException"></exception>
        [Create(@"{projectid:[0-9]+}/contact")]
        public ProjectWrapperFull AddProjectContact(int projectid, int contactid)
        {
            var contact = CrmDaoFactory.ContactDao.GetByID(contactid);
            if (contact == null) throw new ArgumentException();

            var projectEngine = EngineFactory.ProjectEngine;

            var project = projectEngine.GetFullProjectByID(projectid).NotFoundIfNull();
            ProjectSecurity.DemandLinkContact(project);

            projectEngine.AddProjectContact(projectid, contactid);

            var messageAction = contact is Company ? MessageAction.CompanyLinkedProject : MessageAction.PersonLinkedProject;
            MessageService.Send(Request, messageAction, MessageTarget.Create(project.ID), contact.GetTitle(), project.Title);

            return ProjectWrapperFullSelector(project, null);
        }

        ///<summary>
        /// Deletes the selected contact from the project with the ID specified in the request.
        ///</summary>       
        ///<param name="projectid">Project ID</param>
        ///<param name="contactid">Contact ID</param>
        ///<category>Contacts</category>
        ///<short>Delete a project contact</short> 
        ///<returns>Project</returns>
        ///<exception cref="ArgumentException"></exception>
        [Delete("{projectid:[0-9]+}/contact")]
        public ProjectWrapperFull DeleteProjectContact(int projectid, int contactid)
        {
            var contact = CrmDaoFactory.ContactDao.GetByID(contactid);
            if (contact == null) throw new ArgumentException();

            var projectEngine = EngineFactory.ProjectEngine;

            var project = projectEngine.GetByID(projectid).NotFoundIfNull();
            ProjectSecurity.DemandEdit(project);

            projectEngine.DeleteProjectContact(projectid, contactid);

            var messageAction = contact is Company ? MessageAction.CompanyUnlinkedProject : MessageAction.PersonUnlinkedProject;
            MessageService.Send(Request, messageAction, MessageTarget.Create(project.ID), contact.GetTitle(), project.Title);

            return ProjectWrapperFullSelector(project, null);
        }

        #endregion

        #region templates

        ///<summary>
        ///Returns a list of all the templates with the base information about them.
        ///</summary>
        ///<short>
        ///Get templates
        ///</short>
        ///<category>Templates</category>
        ///<returns>List of templates</returns>
        [Read("template")]
        public IEnumerable<object> GetAllTemplates()
        {
            return EngineFactory.TemplateEngine.GetAll().Select(x => new { x.Id, x.Title, x.Description, CanEdit = ProjectSecurity.CanEditTemplate(x) });
        }

        ///<summary>
        ///Returns the detailed information about a template with ID specified in the request.
        ///</summary>
        ///<short>
        ///Get a template by ID
        ///</short>
        ///<category>Templates</category>
        ///<param name="id">Template ID</param>
        ///<returns>Template</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Read(@"template/{id:[0-9]+}")]
        public ObjectWrapperBase GetTemplate(int id)
        {
            var template = EngineFactory.TemplateEngine.GetByID(id).NotFoundIfNull();
            return new ObjectWrapperBase { Id = template.Id, Title = template.Title, Description = template.Description };
        }

        ///<summary>
        ///Creates a new template with the title and description specified in the request. 
        ///</summary>
        ///<short>
        ///Create a template
        ///</short>
        ///<category>Templates</category>
        ///<param name="title">Template title</param>
        ///<param name="description">JSON template structure in the following format: {"tasks":[{"title":"Task without milestone"}],"milestones":[{"title":"milestone title","duration":0.5,"tasks":[{"title":"milestone task"}]}]}</param>
        ///<returns>Newly created template</returns>
        ///<exception cref="ArgumentException"></exception>
        [Create("template")]
        public ObjectWrapperBase CreateTemplate(string title, string description)
        {
            if (string.IsNullOrEmpty(title)) throw new ArgumentException(@"title can't be empty", "title");

            ProjectSecurity.DemandCreate<Project>(null);

            var template = new Template
            {
                Title = title,
                Description = description
            };

            template = EngineFactory.TemplateEngine.SaveOrUpdate(template).NotFoundIfNull();
            MessageService.Send(Request, MessageAction.ProjectTemplateCreated, MessageTarget.Create(template.Id), template.Title);

            return new ObjectWrapperBase { Id = template.Id, Title = template.Title, Description = template.Description };
        }

        ///<summary>
        ///Updates the existing template information with the parameters specified in the request.
        ///</summary>
        ///<short>
        ///Update a template
        ///</short>
        ///<category>Templates</category>
        ///<param name="id">Template ID</param>
        ///<param name="title">New template title</param>
        ///<param name="description">New JSON template structure in the following format: {"tasks":[{"title":"Task without milestone"}],"milestones":[{"title":"milestone title","duration":0.5,"tasks":[{"title":"milestone task"}]}]}</param>
        ///<returns>Updated template</returns>
        ///<exception cref="ArgumentException"></exception>
        ///<exception cref="ItemNotFoundException"></exception>
        [Update(@"template/{id:[0-9]+}")]
        public ObjectWrapperBase UpdateTemplate(int id, string title, string description)
        {
            if (string.IsNullOrEmpty(title)) throw new ArgumentException(@"title can't be empty", "title");

            var templateEngine = EngineFactory.TemplateEngine;

            var template = templateEngine.GetByID(id).NotFoundIfNull();

            template.Title = Update.IfNotEmptyAndNotEquals(template.Title, title);
            template.Description = Update.IfNotEmptyAndNotEquals(template.Description, description);

            templateEngine.SaveOrUpdate(template);
            MessageService.Send(Request, MessageAction.ProjectTemplateUpdated, MessageTarget.Create(template.Id), template.Title);

            return new ObjectWrapperBase { Id = template.Id, Title = template.Title, Description = template.Description };
        }

        ///<summary>
        ///Deletes a template with the ID specified in the request from the portal.
        ///</summary>
        ///<short>
        ///Delete a template
        ///</short>
        ///<category>Templates</category>
        ///<param name="id">Template ID</param>
        ///<returns>Deleted template</returns>
        ///<exception cref="ItemNotFoundException"></exception>
        [Delete(@"template/{id:[0-9]+}")]
        public ObjectWrapperBase DeleteTemplate(int id)
        {
            var templateEngine = EngineFactory.TemplateEngine;
            var template = templateEngine.GetByID(id).NotFoundIfNull();

            templateEngine.Delete(id);
            MessageService.Send(Request, MessageAction.ProjectTemplateDeleted, MessageTarget.Create(template.Id), template.Title);

            return new ObjectWrapperBase { Id = template.Id, Title = template.Title, Description = template.Description };
        }

        #endregion

        #region HACK: Hidden api methods

        ///<summary>
        /// Returns the basic information about the access rights.
        ///</summary>
        ///<short>
        /// Get security information
        ///</short>
        ///<category>Projects</category>
        ///<returns>Basic information about the access rights</returns>
        ///<visible>false</visible>
        [Read("securityinfo")]
        public CommonSecurityInfo GetProjectSecurityInfo()
        {
            return new CommonSecurityInfo();
        }

        ///<summary>
        /// Returns the last modified project.
        ///</summary>
        ///<short>
        /// Get the last modified project
        ///</short>
        ///<category>Projects</category>
        ///<returns>Last modified project</returns>
        ///<visible>false</visible>
        [Read("maxlastmodified")]
        public string GetProjectMaxLastModified()
        {
            var maxModified = EngineFactory.ProjectEngine.GetMaxLastModified();
            var maxTeamModified = EngineFactory.ProjectEngine.GetTeamMaxLastModified();
            var result = DateTime.Compare(maxModified, maxTeamModified) > 0 ? maxModified : maxTeamModified;
            return result + EngineFactory.ProjectEngine.Count().ToString();
        }

        ///<summary>
        /// Returns the current task order in the project with the ID specified in the request.
        ///</summary>
        ///<short>
        /// Get the task order
        ///</short>
        ///<category>Tasks</category>
        ///<param name="id">Project ID</param>
        ///<returns>Task order</returns>
        ///<visible>false</visible>
        [Read(@"{id:[0-9]+}/order")]
        public string GetTaskOrder(int id)
        {
            var projectEngine = EngineFactory.ProjectEngine;
            var project = projectEngine.GetByID(id).NotFoundIfNull();

            return projectEngine.GetTaskOrder(project);
        }

        ///<summary>
        /// Sets the task order to the project with the ID specified in the request.
        ///</summary>
        ///<short>
        /// Set the task order
        ///</short>
        ///<category>Tasks</category>
        ///<param name="id">Project ID</param>
        ///<param name="order">Task order</param>
        ///<returns>Task order</returns>
        ///<visible>false</visible>
        [Update(@"{id:[0-9]+}/order")]
        public void SetTaskOrder(int id, string order)
        {
            if (string.IsNullOrEmpty(order)) throw new ArgumentException(@"order can't be empty", "order");

            var projectEngine = EngineFactory.ProjectEngine;
            var project = projectEngine.GetByID(id).NotFoundIfNull();

            projectEngine.SetTaskOrder(project, order);
        }

        #endregion
    }
}