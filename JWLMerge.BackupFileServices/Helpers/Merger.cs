﻿namespace JWLMerge.BackupFileServices.Helpers
{
    using System;
    using System.Collections.Generic;
    using Events;
    using Models.Database;
    using Serilog;

    /// <summary>
    /// Merges the SQLite databases.
    /// </summary>
    internal sealed class Merger
    {
        private readonly IdTranslator _translatedLocationIds = new IdTranslator();
        private readonly IdTranslator _translatedTagIds = new IdTranslator();
        private readonly IdTranslator _translatedUserMarkIds = new IdTranslator();
        private readonly IdTranslator _translatedNoteIds = new IdTranslator();

        private int _maxLocationId;
        private int _maxUserMarkId;
        private int _maxNoteId;
        private int _maxTagId;
        private int _maxTagMapId;
        private int _maxBlockRangeId;

        public event EventHandler<ProgressEventArgs> ProgressEvent;

        /// <summary>
        /// Merges the specified databases.
        /// </summary>
        /// <param name="databasesToMerge">The databases to merge.</param>
        /// <returns><see cref="Database"/></returns>
        public Database Merge(IEnumerable<Database> databasesToMerge)
        {
            var result = new Database();
            result.InitBlank();

            ClearMaxIds();

            foreach (var database in databasesToMerge)
            {
                Merge(database, result);
            }

            return result;
        }

        private void ClearMaxIds()
        {
            _maxLocationId = 0;
            _maxUserMarkId = 0;
            _maxNoteId = 0;
            _maxTagId = 0;
            _maxTagMapId = 0;
            _maxBlockRangeId = 0;
        }
        
        private void Merge(Database source, Database destination)
        {
            ClearTranslators();
            
            MergeUserMarks(source, destination);
            MergeNotes(source, destination);
            MergeTags(source, destination);
            MergeTagMap(source, destination);
            MergeBlockRanges(source, destination);
            
            destination.ReinitializeIndexes();
        }

        private void ClearTranslators()
        {
            _translatedLocationIds.Clear();
            _translatedTagIds.Clear();
            _translatedUserMarkIds.Clear();
            _translatedNoteIds.Clear();
        }
        
        private void MergeBlockRanges(Database source, Database destination)
        {
            ProgressMessage("Merging block ranges...");

            foreach (var range in source.BlockRanges)
            {
                var userMarkId = _translatedUserMarkIds.GetTranslatedId(range.UserMarkId);
                var existingRange = destination.FindBlockRange(userMarkId);
                if (existingRange == null)
                {
                    InsertBlockRange(range, destination);
                }
            }
        }

        private void MergeTagMap(Database source, Database destination)
        {
            ProgressMessage("Merging tag map...");

            foreach (var tagMap in source.TagMaps)
            {
                if (tagMap.Type == 1)
                {
                    // a tag on a note...
                    var tagId = _translatedTagIds.GetTranslatedId(tagMap.TagId);
                    var noteId = _translatedNoteIds.GetTranslatedId(tagMap.TypeId);
                    
                    var existingTagMap = destination.FindTagMap(tagId, noteId);
                    if (existingTagMap == null)
                    {
                        InsertTagMap(tagMap, destination);
                    }
                }
            }
        }

        private void MergeTags(Database source, Database destination)
        {
            ProgressMessage("Merging tags...");

            foreach (var tag in source.Tags)
            {
                var existingTag = destination.FindTag(tag.Name);
                if (existingTag != null)
                {
                    _translatedTagIds.Add(tag.TagId, existingTag.TagId);
                }
                else
                {
                    InsertTag(tag, destination);
                }
            }
        }

        private void MergeUserMarks(Database source, Database destination)
        {
            ProgressMessage("Merging user marks...");

            foreach (var userMark in source.UserMarks)
            {
                var existingUserMark = destination.FindUserMark(userMark.UserMarkGuid);
                if (existingUserMark != null)
                {
                    // user mark already exists in destination...
                    _translatedUserMarkIds.Add(userMark.UserMarkId, existingUserMark.UserMarkId);
                }
                else
                {
                    var referencedLocation = userMark.LocationId;
                    var location = source.FindLocation(referencedLocation);

                    InsertLocation(location, destination);
                    InsertUserMark(userMark, destination);
                }
            }
        }

        private void InsertLocation(Location location, Database destination)
        {
            if (_translatedLocationIds.GetTranslatedId(location.LocationId) == 0)
            {
                Location newLocation = location.Clone();
                newLocation.LocationId = ++_maxLocationId;
                destination.Locations.Add(newLocation);

                _translatedLocationIds.Add(location.LocationId, newLocation.LocationId);
            }
        }
        
        private void InsertUserMark(UserMark userMark, Database destination)
        {
            UserMark newUserMark = userMark.Clone();
            newUserMark.UserMarkId = ++_maxUserMarkId;
            newUserMark.LocationId = _translatedLocationIds.GetTranslatedId(userMark.LocationId);
            destination.UserMarks.Add(newUserMark);
            
            _translatedUserMarkIds.Add(userMark.UserMarkId, newUserMark.UserMarkId);
        }

        private void InsertTag(Tag tag, Database destination)
        {
            Tag newTag = tag.Clone();
            newTag.TagId = ++_maxTagId;
            destination.Tags.Add(newTag);

            _translatedTagIds.Add(tag.TagId, newTag.TagId);
        }

        private void InsertTagMap(TagMap tagMap, Database destination)
        {
            TagMap newTagMap = tagMap.Clone();
            newTagMap.TagMapId = ++_maxTagMapId;
            newTagMap.TagId = _translatedTagIds.GetTranslatedId(tagMap.TagId);
            newTagMap.TypeId = _translatedNoteIds.GetTranslatedId(tagMap.TypeId);

            destination.TagMaps.Add(newTagMap);
        }

        private void InsertNote(Note note, Database destination)
        {
            Note newNote = note.Clone();
            newNote.NoteId = ++_maxNoteId;

            if (note.UserMarkId != null)
            {
                newNote.UserMarkId = _translatedUserMarkIds.GetTranslatedId(note.UserMarkId.Value);
            }

            if (note.LocationId != null)
            {
                newNote.LocationId = _translatedLocationIds.GetTranslatedId(note.LocationId.Value);
            }
            
            destination.Notes.Add(newNote);
            _translatedNoteIds.Add(note.NoteId, newNote.NoteId);
        }

        private void InsertBlockRange(BlockRange range, Database destination)
        {
            BlockRange newRange = range.Clone();
            newRange.BlockRangeId = ++_maxBlockRangeId;

            newRange.UserMarkId = _translatedUserMarkIds.GetTranslatedId(range.UserMarkId);
            destination.BlockRanges.Add(newRange);
        }

        private void MergeNotes(Database source, Database destination)
        {
            ProgressMessage("Merging notes...");
            
            foreach (var note in source.Notes)
            {
                var existingNote = destination.FindNote(note.Guid);
                if (existingNote != null)
                {
                    // note already exists in destination...
                    if (existingNote.GetLastModifiedDateTime() < note.GetLastModifiedDateTime())
                    {
                        // ...but it's older
                        UpdateNote(note, existingNote);
                    }

                    _translatedNoteIds.Add(note.NoteId, existingNote.NoteId);
                }
                else
                {
                    // a new note...
                    if (note.LocationId != null && _translatedLocationIds.GetTranslatedId(note.LocationId.Value) == 0)
                    {
                        var referencedLocation = note.LocationId.Value;
                        var location = source.FindLocation(referencedLocation);

                        InsertLocation(location, destination);
                    }
                    
                    InsertNote(note, destination);
                }
            }
        }

        private void UpdateNote(Note source, Note destination)
        {
            destination.Title = source.Title;
            destination.Content = source.Content;
            destination.LastModified = source.LastModified;
        }

        private void OnProgressEvent(string message)
        {
            ProgressEvent?.Invoke(this, new ProgressEventArgs { Message = message });
        }
        
        private void ProgressMessage(string logMessage)
        {
            Log.Logger.Information(logMessage);
            OnProgressEvent(logMessage);
        }
    }
}