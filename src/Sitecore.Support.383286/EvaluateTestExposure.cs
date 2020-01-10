using Sitecore.Analytics;
using Sitecore.Analytics.DataAccess;
using Sitecore.ContentTesting;
using Sitecore.ContentTesting.Data;
using Sitecore.ContentTesting.Diagnostics;
using Sitecore.ContentTesting.Inspectors;
using Sitecore.ContentTesting.Model.Data.Items;
using Sitecore.ContentTesting.Models;
using Sitecore.ContentTesting.Pipelines;
using Sitecore.ContentTesting.Pipelines.GetCurrentTestCombination;
using Sitecore.ContentTesting.Pipelines.SuspendTest;
using Sitecore.ContentTesting.Web;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Pipelines.RenderLayout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Sitecore.Support.ContentTesting.Pipelines.RenderLayout
{
    public class EvaluateTestExposure : EvaluateTestExposureBase<RenderLayoutArgs>
    {
        private new readonly IContentTestStore contentTestStore;

        public EvaluateTestExposure()
            : base((IContentTestStore)null, (IContentTestingFactory)null)
        {
        }

        public EvaluateTestExposure(IContentTestStore contentTestStore, IContentTestingFactory factory)
            : base(contentTestStore, factory)
        {
        }

        protected override Item GetRequestItem(RenderLayoutArgs args)
        {
            return Context.Item;
        }

        [Obsolete("Use FindTestForItem() method instead.")]
        protected virtual IEnumerable<TestDefinitionItem> FindTestsForItem(Item item, ID deviceId)
        {
            Assert.ArgumentNotNull(item, "item");
            Assert.ArgumentNotNull(deviceId, "deviceId");
            ITestConfiguration testConfiguration = contentTestStore.LoadTestForItem(item, deviceId);
            List<TestDefinitionItem> list = new List<TestDefinitionItem>();
            if (testConfiguration != null && testConfiguration.TestDefinitionItem != null)
            {
                list.Add(testConfiguration.TestDefinitionItem);
            }
            return list;
        }
        public new void Process(RenderLayoutArgs args)
        {
            Assert.ArgumentNotNull(args, "args");
            if (!Sitecore.ContentTesting.Configuration.Settings.IsAutomaticContentTestingEnabled || (Context.Site != null && Context.Site.Name == "shell"))
            {
                return;
            }
            Item requestItem = GetRequestItem(args);
            if (requestItem != null)
            {
                ITestConfiguration testConfiguration = FindTestForItem(requestItem, Context.Device.ID);
                if (testConfiguration != null && testConfiguration.TestDefinitionItem.IsRunning && !(Tracker.Current?.Contact.System.Classification >= 900))
                {
                    try
                    {
                        TestCombinationContextBase testCombinationContext = factory.GetTestCombinationContext(new HttpContextWrapper(HttpContext.Current));
                        TestSet testSet = TestManager.GetTestSet(new TestDefinitionItem[1]
                        {
                        testConfiguration.TestDefinitionItem
                        }, requestItem, Context.Device.ID);
                        if (factory.EditModeContext.TestCombination != null)
                        {
                            TestCombination combination = new TestCombination(factory.EditModeContext.TestCombination, testSet);
                            if (!ValidateCombinationDatasource(combination, testConfiguration))
                            {
                                factory.TestingTracker.ClearMvTest();
                                testCombinationContext.SaveToResponse(testSet.Id, null);
                            }
                            else
                            {
                                factory.TestingTracker.SetTestCombination(combination, testConfiguration.TestDefinitionItem, firstExposure: false);
                            }
                        }
                        else if (!Context.PageMode.IsExperienceEditor && Tracker.Current != null && Tracker.IsActive)
                        {
                            if (!testCombinationContext.IsSetInRequest())
                            {
                                goto IL_0201;
                            }
                            byte[] fromRequest = testCombinationContext.GetFromRequest(testSet.Id);
                            if (fromRequest == null || fromRequest.Length != testSet.Variables.Count)
                            {
                                goto IL_0201;
                            }
                            bool flag = true;
                            for (int i = 0; i < fromRequest.Length; i++)
                            {
                                flag = (flag && fromRequest[i] <= testSet.Variables[i].Values.Count - 1);
                            }
                            if (!flag)
                            {
                                goto IL_0201;
                            }
                            TestCombination combination2 = new TestCombination(fromRequest, testSet);
                            if (!ValidateCombinationDatasource(combination2, testConfiguration))
                            {
                                factory.TestingTracker.ClearMvTest();
                                testCombinationContext.SaveToResponse(testSet.Id, null);
                            }
                            else
                            {
                                factory.TestingTracker.SetTestCombination(combination2, testConfiguration.TestDefinitionItem, firstExposure: false);
                            }
                        }
                        goto end_IL_0066;
                    IL_0201:
                        if (ShouldIncludeRequestByTrafficAllocation(requestItem, testConfiguration))
                        {
                            GetCurrentTestCombinationArgs getCurrentTestCombinationArgs = new GetCurrentTestCombinationArgs(new TestDefinitionItem[1]
                            {
                            testConfiguration.TestDefinitionItem
                            })
                            {
                                Item = requestItem,
                                DeviceID = Context.Device.ID
                            };
                            SettingsDependantPipeline<GetCurrentTestCombinationPipeline, GetCurrentTestCombinationArgs>.Instance.Run(getCurrentTestCombinationArgs);
                            if (getCurrentTestCombinationArgs.Combination != null)
                            {
                                if (!ValidateCombinationDatasource(getCurrentTestCombinationArgs.Combination, testConfiguration))
                                {
                                    factory.TestingTracker.ClearMvTest();
                                    testCombinationContext.SaveToResponse(testSet.Id, null);
                                }
                                else
                                {
                                    factory.TestingTracker.SetTestCombination(getCurrentTestCombinationArgs.Combination, testConfiguration.TestDefinitionItem);
                                    testCombinationContext.SaveToResponse(getCurrentTestCombinationArgs.Combination.Testset.Id, getCurrentTestCombinationArgs.Combination.Combination);
                                }
                            }
                        }
                        else
                        {
                            testCombinationContext.SaveToResponse(testSet.Id, null);
                        }
                    end_IL_0066:;
                    }
                    catch (XdbUnavailableException exception)
                    {
                        Logger.Error("Failed to evaluate test exposure due to xDB unavailable.", exception, this);
                    }
                    catch (Exception exception2)
                    {
                        Logger.Error("General error when evaluating test exposure.", exception2, this);
                    }
                }
            }
        }

        private static bool ValidateCombinationDatasource(TestCombination combination, ITestConfiguration testConfiguration)
        {
            TestValueInspector testValueInspector = new TestValueInspector();
            for (int i = 0; i < combination.Combination.Length; i++)
            {
                if (!combination.TryGetValue(i, out TestValue value))
                {
                    return false;
                }
                if (!testValueInspector.IsValidDataSource(testConfiguration.TestDefinitionItem, value))
                {
                    SuspendTestArgs args = new SuspendTestArgs(testConfiguration);
                    SettingsDependantPipeline<SuspendTestPipeline, SuspendTestArgs>.Instance.Run(args);
                    return false;
                }
            }
            return true;
        }
    }
}