using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace YanickSenn.TableView.Editor
{
    public class ScriptableObjectTableView : EditorWindow
    {
        [MenuItem("Window/Scriptable Object Table View")]
        public static void ShowWindow()
        {
            GetWindow<ScriptableObjectTableView>("Table View");
        }

        private MultiColumnListView _listView;
        private Label _statusLabel;
        private Button _createButton;
        
        private Type _currentType;
        private List<ScriptableObject> _objects;
        private List<SerializedObject> _serializedObjects;
        
        // Cache property paths to build columns once
        private List<string> _propertyPaths;
        private List<string> _propertyDisplayNames;

        private void OnEnable()
        {
            EditorApplication.projectChanged += OnProjectChanged;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
        }

        private void OnProjectChanged()
        {
            if (_currentType != null)
            {
                LoadTableForType(_currentType);
            }
        }

        private void CreateGUI()
        {
            _statusLabel = new Label("Select a ScriptableObject to view table.")
            {
                style =
                {
                    paddingTop = 10,
                    paddingBottom = 10,
                    paddingLeft = 10,
                    paddingRight = 10,
                    unityTextAlign = TextAnchor.MiddleCenter
                }
            };
            rootVisualElement.Add(_statusLabel);

            _listView = new MultiColumnListView
            {
                style = { flexGrow = 1, display = DisplayStyle.None },
                showAlternatingRowBackgrounds = AlternatingRowBackground.All,
            };
            rootVisualElement.Add(_listView);

            _createButton = new Button(OnCreateButtonClicked)
            {
                text = "Create New",
                style = { display = DisplayStyle.None, height = 30, marginTop = 5, marginBottom = 5 }
            };
            rootVisualElement.Add(_createButton);

            // Initial check
            OnSelectionChange();
        }

        private void OnCreateButtonClicked()
        {
            if (_currentType == null) return;

            // Determine file name from attribute or type name
            var attribute = _currentType.GetCustomAttributes(typeof(CreateAssetMenuAttribute), true)
                .FirstOrDefault() as CreateAssetMenuAttribute;
            var baseName = attribute != null && !string.IsNullOrEmpty(attribute.fileName) 
                ? attribute.fileName 
                : _currentType.Name;

            // Determine directory from currently selected object
            var activePath = AssetDatabase.GetAssetPath(Selection.activeObject);
            var directory = string.IsNullOrEmpty(activePath) ? "Assets" : System.IO.Path.GetDirectoryName(activePath);
            
            var path = $"{directory}/{baseName}.asset";
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            var instance = CreateInstance(_currentType);
            AssetDatabase.CreateAsset(instance, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            // Optionally select the new asset
            Selection.activeObject = instance;
        }

        private void OnSelectionChange()
        {
            var activeObj = Selection.activeObject;
            
            // Basic validation: Must be a ScriptableObject and existing asset (not scene object, though SOs are usually assets)
            if (activeObj is ScriptableObject so && EditorUtility.IsPersistent(activeObj))
            {
                var type = so.GetType();
                // If type changed or we want to force refresh (e.g. added new files)
                // For now, let's refresh if type is different or if we assume list might have changed.
                // To be safe and responsive, we can just rebuild.
                LoadTableForType(type);
            }
            else
            {
                ClearTable();
            }
        }

        private void ClearTable()
        {
            _listView.itemsSource = null;

            _currentType = null;
            _objects = null;
            _serializedObjects = null;
            _propertyPaths = null;
            _propertyDisplayNames = null;

            _listView.columns.Clear();
            _listView.Rebuild();
            
            _listView.style.display = DisplayStyle.None;
            _createButton.style.display = DisplayStyle.None;
            _statusLabel.style.display = DisplayStyle.Flex;
            _statusLabel.text = "Select a ScriptableObject to view table.";
        }

        private void LoadTableForType(Type type)
        {
            _currentType = type;
            _statusLabel.style.display = DisplayStyle.None;
            _listView.style.display = DisplayStyle.Flex;
            _createButton.style.display = DisplayStyle.Flex;

            // 1. Find all assets of this type
            var guids = AssetDatabase.FindAssets($"t:{type.Name}");
            _objects = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ScriptableObject>)
                .Where(o => o != null && o.GetType() == type) // Strict type check to avoid mixed inheritance tables for now
                .ToList();

            _serializedObjects = _objects.Select(o => new SerializedObject(o)).ToList();

            // 2. Build Columns based on the first object (or a temp one if list empty?)
            // If list is empty, we can't really build columns effectively without reflection or a dummy instance.
            // But if we selected one, the list has at least one.
            if (_objects.Count > 0)
            {
                BuildColumns(_serializedObjects[0]);
            }

            // 3. Bind
            _listView.itemsSource = _serializedObjects;
            _listView.Rebuild();
        }

        private void BuildColumns(SerializedObject sampleSo)
        {
            _listView.columns.Clear();
            _propertyPaths = new List<string>();
            _propertyDisplayNames = new List<string>();
            
            // Column 0: Actions
            var actionsColumn = new Column
            {
                name = "",
                title = "",
                width = 25,
                minWidth = 25,
                maxWidth = 25,
                makeCell = () =>
                {
                    var button = new Button { text = "..." };
                    button.clicked += () =>
                    {
                        var index = (int)button.userData;
                        if (index >= 0 && index < _serializedObjects.Count)
                        {
                            var so = _serializedObjects[index];
                            var menu = new GenericMenu();
                            menu.AddItem(new GUIContent("Delete"), false, () =>
                            {
                                if (EditorUtility.DisplayDialog("Delete Asset", 
                                        $"Are you sure you want to delete '{so.targetObject.name}'?", "Yes", "No"))
                                {
                                    var path = AssetDatabase.GetAssetPath(so.targetObject);
                                    AssetDatabase.DeleteAsset(path);
                                    AssetDatabase.Refresh();
                                }
                            });
                            menu.ShowAsContext();
                        }
                    };
                    return button;
                },
                bindCell = (element, index) =>
                {
                    var button = (Button)element;
                    button.userData = index;
                }
            };
            _listView.columns.Add(actionsColumn);
            
            // Column 1: Asset Object
            var assetColumn = new Column
            {
                name = "Asset",
                title = "Asset",
                width = 150,
                minWidth = 100,
                makeCell = () =>
                {
                    var field = new ObjectField { objectType = typeof(ScriptableObject), label = "" };
                    field.SetEnabled(false);
                    return field;
                },
                bindCell = (element, index) =>
                {
                    var field = (ObjectField)element;
                    if (_serializedObjects != null && index >= 0 && index < _serializedObjects.Count)
                    {
                        field.value = _serializedObjects[index].targetObject;
                    }
                }
            };
            _listView.columns.Add(assetColumn);

            // Column 2: Asset Name
            var nameColumn = new Column
            {
                name = "AssetName",
                title = "Asset Name",
                width = 150,
                minWidth = 100,
                makeCell = () =>
                {
                    var textField = new TextField();
                    textField.isDelayed = true; // Only rename on Enter/FocusOut
                    textField.RegisterValueChangedCallback(evt =>
                    {
                        var tf = (TextField)evt.target;
                        var index = (int)tf.userData;
                        
                        // Safety check in case list changed
                        if (index >= 0 && index < _serializedObjects.Count)
                        {
                            var so = _serializedObjects[index];
                            if (so != null && so.targetObject != null && evt.newValue != so.targetObject.name)
                            {
                                var path = AssetDatabase.GetAssetPath(so.targetObject);
                                AssetDatabase.RenameAsset(path, evt.newValue);
                                // Note: Renaming might invalidate the path or object reference in some contexts,
                                // but SerializedObject usually holds the ID.
                            }
                        }
                    });
                    return textField;
                },
                bindCell = (element, index) =>
                {
                    var textField = (TextField)element;
                    textField.userData = index;
                    if (_serializedObjects != null && index >= 0 && index < _serializedObjects.Count)
                    {
                        var so = _serializedObjects[index];
                        textField.value = so.targetObject != null ? so.targetObject.name : "";
                    }
                }
            };
            _listView.columns.Add(nameColumn);

            // Iterate properties
            var iterator = sampleSo.GetIterator();
            bool enterChildren = true;
            
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false; // Don't drill deep into arrays/nested structs automatically for columns, keep it top-level

                if (iterator.name == "m_Script") continue; // Skip script reference

                var path = iterator.propertyPath;
                var title = iterator.displayName;
                
                _propertyPaths.Add(path);
                _propertyDisplayNames.Add(title);

                // Check for TextArea attribute to override default PropertyField behavior
                System.Reflection.FieldInfo field = null;
                var typeToCheck = _currentType;
                while (typeToCheck != null && field == null)
                {
                    field = typeToCheck.GetField(iterator.name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    typeToCheck = typeToCheck.BaseType;
                }
                
                bool isTextArea = field != null && Attribute.IsDefined(field, typeof(TextAreaAttribute)) && iterator.propertyType == SerializedPropertyType.String;

                var column = new Column
                {
                    name = path,
                    title = title,
                    width = 150,
                    minWidth = 50,
                    makeCell = () =>
                    {
                        var el = isTextArea ? (VisualElement)new TextField() : new PropertyField();
                        if (el is PropertyField pf) pf.label = "";
                        if (el is TextField tf) tf.label = "";
                        return el;
                    },
                    bindCell = (element, index) =>
                    {
                        if (_serializedObjects != null && index >= 0 && index < _serializedObjects.Count)
                        {
                            var so = _serializedObjects[index];
                            var prop = so.FindProperty(path);
                            if (prop != null)
                            {
                                if (element is PropertyField propField)
                                {
                                    propField.BindProperty(prop);
                                }
                                else if (element is IBindable bindable)
                                {
                                    bindable.BindProperty(prop);
                                }
                            }
                            else
                            {
                                element.Unbind();
                            }
                        }
                        else
                        {
                            element.Unbind();
                        }
                    }
                };
                _listView.columns.Add(column);
            }
        }
    }
}
